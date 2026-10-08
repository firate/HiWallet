using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Actors;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Accounts;

/// <summary>
/// Müşteri seviyesinin aylık limitlerini ve bu ay ne kadarını kullandığını görüyor. Gösterilen
/// sayı limit kontrolünün kullandığı sayının aynısı: transfer, ödeme ve çekim reddi bu
/// kullanıma bakarak veriliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountLimitsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Account, Guid Wallet)> PersonAsync(KycLevel level, decimal funds, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, level);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);

        if (funds > 0m)
        {
            await LedgerSeeder.FundAsync(db, wallet, funds, ct);
        }

        return (account, wallet);
    }

    private async Task TransferAsync(Guid ownerAccount, Guid from, Guid to, decimal amount, CancellationToken ct)
    {
        using var client = _factory.CreateClient().AsOwnerOf(ownerAccount);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId = from, toWalletId = to, amount, currency = "TRY", type = "P2P" }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString("N") } }
        };

        var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
    }

    /// <summary>%2 komisyon, en az 2 TRY: 100 çekim cüzdandan 102 düşüyor.</summary>
    private async Task WithdrawAsync(Guid account, Guid wallet, decimal amount, CancellationToken ct)
    {
        var handler = new DebitForWithdrawalHandler(
            postgres.ContextFactory,
            new WithdrawalPolicy(new CommissionRate(0.02m, Minimum: 2m), new TransferLimit(null, Daily: 50_000m)),
            TestKycLimits.Policy,
            new SystemClock(),
            NullLogger<DebitForWithdrawalHandler>.Instance);

        await handler.HandleAsync(new DebitForWithdrawal
        {
            CommandId = Guid.NewGuid(),
            SagaId = Guid.NewGuid(),
            WalletId = wallet,
            Amount = amount,
            Currency = "TRY",
            Actor = new CommandActor
            {
                Type = ActorTypes.Customer, Id = account.ToString(), Subject = TestTokens.SubjectOf(account)
            }
        }, ct);
    }

    private static JsonElement Movement(JsonElement limits, string movement) =>
        limits.GetProperty("movements").EnumerateArray()
            .Single(item => item.GetProperty("movement").GetString() == movement);

    [Fact]
    public async Task Limitler_SeviyeninTarifesiniVeBuAyKullanilaniGosterir()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(KycLevel.Verified, 1_000m, ct);
        var other = await PersonAsync(KycLevel.Contracted, 1_000m, ct);

        await TransferAsync(person.Account, person.Wallet, other.Wallet, 100m, ct);
        await TransferAsync(other.Account, other.Wallet, person.Wallet, 50m, ct);
        await WithdrawAsync(person.Account, person.Wallet, 100m, ct);

        decimal sent;
        await using (var db = postgres.CreateContext())
        {
            // Giden transferin kullanımı cüzdandan düşen: tutar ve komisyon.
            sent = 1_000m + 50m - 102m - await LedgerSeeder.BalanceAsync(db, person.Wallet, ct);
        }

        using var client = _factory.CreateClient().AsOwnerOf(person.Account);
        var response = await client.GetAsync($"/v1/accounts/{person.Account}/limits?currency=TRY", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
        var limits = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        limits.GetProperty("kycLevel").GetString().ShouldBe("Verified");
        limits.GetProperty("currency").GetString().ShouldBe("TRY");

        var outgoing = Movement(limits, "OutgoingTransfer");
        outgoing.GetProperty("limit").GetDecimal().ShouldBe(25_000m);
        outgoing.GetProperty("used").GetDecimal().ShouldBe(sent);
        outgoing.GetProperty("remaining").GetDecimal().ShouldBe(25_000m - sent);

        Movement(limits, "IncomingTransfer").GetProperty("used").GetDecimal().ShouldBe(50m);
        Movement(limits, "IncomingTotal").GetProperty("used").GetDecimal().ShouldBe(50m);
        Movement(limits, "IncomingTotal").GetProperty("limit").GetDecimal().ShouldBe(100_000m);
        Movement(limits, "Withdrawal").GetProperty("used").GetDecimal().ShouldBe(102m);
        Movement(limits, "Withdrawal").GetProperty("limit").GetDecimal().ShouldBe(25_000m);
        Movement(limits, "Payment").GetProperty("used").GetDecimal().ShouldBe(0m);
        Movement(limits, "Deposit").GetProperty("used").GetDecimal().ShouldBe(0m);

        // Kimliği tespit edilmiş seviyede bakiye tavanı yok.
        limits.GetProperty("balanceCap").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Limitler_TespitEdilmemisSeviyedeKapaliHareketleriVeBakiyeTavaniniGosterir()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(KycLevel.Unverified, 300m, ct);

        using var client = _factory.CreateClient().AsOwnerOf(person.Account);
        var limits = await client.GetFromJsonAsync<JsonElement>(
            $"/v1/accounts/{person.Account}/limits?currency=TRY", ct);

        Movement(limits, "OutgoingTransfer").GetProperty("remaining").GetDecimal().ShouldBe(0m);
        Movement(limits, "Withdrawal").GetProperty("remaining").GetDecimal().ShouldBe(0m);
        Movement(limits, "Payment").GetProperty("limit").GetDecimal().ShouldBe(5_500m);
        limits.GetProperty("balanceCap").GetDecimal().ShouldBe(5_500m);
        limits.GetProperty("balance").GetDecimal().ShouldBe(300m);
    }

    /// <summary>Başkasının hesabının var olduğu da dışarı verilmiyor.</summary>
    [Fact]
    public async Task Limitler_BaskasininHesabinda404()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(KycLevel.Verified, 0m, ct);
        var other = await PersonAsync(KycLevel.Verified, 0m, ct);

        using var client = _factory.CreateClient().AsOwnerOf(other.Account);
        var response = await client.GetAsync($"/v1/accounts/{person.Account}/limits?currency=TRY", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Limitler_IsyeriHesabindaSeviyeYok422()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid business;
        await using (var db = postgres.CreateContext())
        {
            business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
        }

        using var client = _factory.CreateClient().AsOwnerOf(business);
        var response = await client.GetAsync($"/v1/accounts/{business}/limits?currency=TRY", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString()
            .ShouldBe("kyc_level_not_applicable");
    }

    /// <summary>Çalışan müşteriyle konuşurken onun limitini görüyor.</summary>
    [Fact]
    public async Task Limitler_MusteriKaydiniGorenCalisanGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(KycLevel.Verified, 0m, ct);

        using var client = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);
        var response = await client.GetAsync($"/v1/accounts/{person.Account}/limits?currency=TRY", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));
    }
}

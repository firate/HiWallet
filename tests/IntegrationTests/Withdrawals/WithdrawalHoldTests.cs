using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Actors;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Çekim bekletmesi: telefon numarası değişince hesaptan bankaya para bir süre çıkmıyor.
/// Hesabı ele geçiren kişi numarayı değiştirip parayı çekemesin; gerçek müşteri bildirimi
/// görüp itiraz edecek zamanı bulsun. Bekletmeyi yalnızca onboarding koyuyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalHoldTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Account, Guid Wallet)> FundedAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        await LedgerSeeder.FundAsync(db, wallet, 1_000m, ct);
        return (account, wallet);
    }

    private Task<HttpResponseMessage> HoldAsync(HttpClient client, Guid account, DateTimeOffset until, CancellationToken ct) =>
        client.PutAsJsonAsync($"/v1/accounts/{account}/withdrawal-hold", new { until }, ct);

    private async Task<WithdrawalReply> WithdrawAsync(Guid account, Guid wallet, CancellationToken ct) =>
        await new DebitForWithdrawalHandler(
                postgres.ContextFactory,
                new WithdrawalPolicy(new CommissionRate(0m), new TransferLimit(null, Daily: 50_000m)),
                TestKycLimits.Policy,
                new SystemClock(),
                NullLogger<DebitForWithdrawalHandler>.Instance)
            .HandleAsync(new DebitForWithdrawal
            {
                CommandId = Guid.NewGuid(),
                SagaId = Guid.NewGuid(),
                WalletId = wallet,
                Amount = 100m,
                Currency = "TRY",
                Actor = new CommandActor
                {
                    Type = ActorTypes.Customer, Id = account.ToString(), Subject = TestTokens.SubjectOf(account)
                }
            }, ct);

    [Fact]
    public async Task Bekletme_CekimiReddeder_HesaptaGorunur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, wallet) = await FundedAsync(ct);
        var until = DateTimeOffset.UtcNow.AddHours(24);

        using var onboarding = _factory.CreateClient().AsOnboarding();
        var hold = await HoldAsync(onboarding, account, until, ct);
        hold.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _factory.Errors));

        var reply = await WithdrawAsync(account, wallet, ct);
        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebitRejected));
        JsonNode.Parse(reply.Payload)?["rule"]?.GetValue<string>().ShouldBe("withdrawal_hold");

        using var owner = _factory.CreateClient().AsOwnerOf(account);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/v1/accounts/{account}", ct);
        detail.GetProperty("withdrawalHoldUntil").GetDateTimeOffset().ShouldBe(until, TimeSpan.FromSeconds(1));
    }

    /// <summary>Bekletme yalnızca uzuyor: ikinci bir değişiklik öncekinin süresini kısaltamaz.</summary>
    [Fact]
    public async Task Bekletme_Kisaltilamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, _) = await FundedAsync(ct);
        var later = DateTimeOffset.UtcNow.AddHours(24);

        using var onboarding = _factory.CreateClient().AsOnboarding();
        await HoldAsync(onboarding, account, later, ct);
        var shorter = await HoldAsync(onboarding, account, DateTimeOffset.UtcNow.AddHours(1), ct);

        (await shorter.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalHoldUntil")
            .GetDateTimeOffset().ShouldBe(later, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SuresiDolanBekletme_CekimeEngelOlmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, wallet) = await FundedAsync(ct);

        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE accounts SET withdrawal_hold_until = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE id = {account}", ct);
        }

        var reply = await WithdrawAsync(account, wallet, ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalDebited));
    }

    /// <summary>Müşteri kendi hesabına bekletme koyamaz, kaldıramaz da: yalnızca onboarding.</summary>
    [Fact]
    public async Task Musteri_BekletmeKoyamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (account, _) = await FundedAsync(ct);

        using var owner = _factory.CreateClient().AsOwnerOf(account);
        var response = await HoldAsync(owner, account, DateTimeOffset.UtcNow.AddHours(1), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

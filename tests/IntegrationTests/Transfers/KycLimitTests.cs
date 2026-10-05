using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.IntegrationTests.Transfers;

/// <summary>
/// Doğrulama seviyesine göre aylık limitler (wallet-api'nin appsettings'indeki tarife):
/// <c>Unknown</c>'da hiçbir hareket yok; <c>Unverified</c>'da gelen transfer ve ödeme
/// ayda 5.500'e kadar, bakiye de 5.500'ü geçemiyor, başka birine giden transfer kapalı.
/// Hesabın kendi cüzdanları arasındaki aktarım başka birine gönderim değil, seviyeye
/// takılmıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class KycLimitTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task<Guid> MerchantWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
        return await LedgerSeeder.CreateWalletAsync(db, account, "Dükkan", ct);
    }

    private async Task<HttpResponseMessage> TransferAsync(
        Guid ownerAccount, Guid from, Guid to, decimal amount, string type, CancellationToken ct)
    {
        using var client = _factory.CreateClient().AsOwnerOf(ownerAccount);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId = from, toWalletId = to, amount, currency = "TRY", type }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString("N") } }
        };

        return await client.SendAsync(request, ct);
    }

    private static async Task<string?> RuleAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString();

    [Fact]
    public async Task Unknown_GonderemezVeAlamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknown = await PersonAsync(KycLevel.Unknown, 100m, ct);
        var other = await PersonAsync(KycLevel.Contracted, 100m, ct);

        var outgoing = await TransferAsync(unknown.Account, unknown.Wallet, other.Wallet, 10m, "P2P", ct);
        var incoming = await TransferAsync(other.Account, other.Wallet, unknown.Wallet, 10m, "P2P", ct);

        outgoing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(outgoing, ct)).ShouldBe("Kyc.OutgoingTransfer.Monthly");
        incoming.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(incoming, ct)).ShouldBe("Kyc.IncomingTransfer.Monthly");
    }

    [Fact]
    public async Task Unverified_BaskasinaGonderemez_KendiCuzdaninaAktarabilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var unverified = await PersonAsync(KycLevel.Unverified, 100m, ct);
        var other = await PersonAsync(KycLevel.Contracted, 0m, ct);
        Guid savings;

        await using (var db = postgres.CreateContext())
        {
            savings = await LedgerSeeder.CreateWalletAsync(db, unverified.Account, "Birikim", ct);
        }

        var toOther = await TransferAsync(unverified.Account, unverified.Wallet, other.Wallet, 10m, "P2P", ct);
        var toOwn = await TransferAsync(unverified.Account, unverified.Wallet, savings, 10m, "P2P", ct);

        toOther.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(toOther, ct)).ShouldBe("Kyc.OutgoingTransfer.Monthly");
        toOwn.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
    }

    [Fact]
    public async Task Unverified_AylikGelenLimitiKadarAlir()
    {
        var ct = TestContext.Current.CancellationToken;
        var unverified = await PersonAsync(KycLevel.Unverified, 0m, ct);
        var sender = await PersonAsync(KycLevel.Contracted, 10_000m, ct);

        var first = await TransferAsync(sender.Account, sender.Wallet, unverified.Wallet, 3_000m, "P2P", ct);
        var upToLimit = await TransferAsync(sender.Account, sender.Wallet, unverified.Wallet, 2_500m, "P2P", ct);
        var overLimit = await TransferAsync(sender.Account, sender.Wallet, unverified.Wallet, 0.01m, "P2P", ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        upToLimit.StatusCode.ShouldBe(HttpStatusCode.Created);
        overLimit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(overLimit, ct)).ShouldBe("Kyc.IncomingTransfer.Monthly");
    }

    /// <summary>
    /// Ödeme limiti cüzdandan çıkan toplama uygulanıyor, komisyon dahil (%2): 3.000'lik
    /// ödeme 3.060 düşüyor, ardından 2.400'lük ödeme 2.448 ile toplamı 5.508'e çıkarıyor.
    /// </summary>
    [Fact]
    public async Task Unverified_AylikOdemeLimitiKadarOder()
    {
        var ct = TestContext.Current.CancellationToken;
        var unverified = await PersonAsync(KycLevel.Unverified, 5_500m, ct);
        var shop = await MerchantWalletAsync(ct);

        var first = await TransferAsync(unverified.Account, unverified.Wallet, shop, 3_000m, "Payment", ct);
        var overLimit = await TransferAsync(unverified.Account, unverified.Wallet, shop, 2_400m, "Payment", ct);
        var underLimit = await TransferAsync(unverified.Account, unverified.Wallet, shop, 2_390m, "Payment", ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        overLimit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(overLimit, ct)).ShouldBe("Kyc.Payment.Monthly");
        underLimit.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// Bakiye tavanı ayın girişinden bağımsız: önceki aylarda gelmiş ve harcanmamış para
    /// da sayılıyor. Hata alıcıyı söylemiyor, kuralın adını söylüyor.
    /// </summary>
    [Fact]
    public async Task Unverified_BakiyeTavaniniAsanGelenReddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var unverified = await PersonAsync(KycLevel.Unverified, 5_000m, ct);
        var sender = await PersonAsync(KycLevel.Contracted, 1_000m, ct);

        var overCap = await TransferAsync(sender.Account, sender.Wallet, unverified.Wallet, 600m, "P2P", ct);
        var upToCap = await TransferAsync(sender.Account, sender.Wallet, unverified.Wallet, 500m, "P2P", ct);

        overCap.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(overCap, ct)).ShouldBe("Kyc.Balance");
        upToCap.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
    }

    [Fact]
    public async Task Contracted_GonderirVeAlir()
    {
        var ct = TestContext.Current.CancellationToken;
        var contracted = await PersonAsync(KycLevel.Contracted, 10_000m, ct);
        var other = await PersonAsync(KycLevel.Contracted, 0m, ct);

        var response = await TransferAsync(contracted.Account, contracted.Wallet, other.Wallet, 5_000m, "P2P", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
    }
}

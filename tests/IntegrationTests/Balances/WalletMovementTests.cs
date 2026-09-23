using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;

namespace HiWallet.IntegrationTests.Balances;

/// <summary>
/// Cüzdan hareketleri: <c>ledger_entries</c>'in cüzdana ait satırları, yeniden
/// eskiye. Sayfalama CURSOR ile — ledger append-only ve yeni satırlar listenin
/// BAŞINA giriyor, offset kullansaydık iki sayfa arasında gelen bir top-up
/// müşteriye aynı kaydı iki kez gösterirdi.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WalletMovementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> WalletWithMovementsAsync(int count, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, "Birikim", ct);

        for (var i = 1; i <= count; i++)
        {
            await LedgerSeeder.FundAsync(db, walletId, i * 10m, ct);
        }

        return walletId;
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        var response = await _client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    [Fact]
    public async Task Hareketler_YenidenEskiye_Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(3, ct);

        var page = await GetAsync($"/v1/wallets/{wallet}/movements", ct);
        var items = page.GetProperty("items").EnumerateArray().ToArray();

        items.Length.ShouldBe(3);

        // Son yüklenen 30, ilk sırada.
        items[0].GetProperty("amount").GetDecimal().ShouldBe(30m);
        items[2].GetProperty("amount").GetDecimal().ShouldBe(10m);
    }

    [Fact]
    public async Task Cursor_SonrakiSayfayiGetirir_TekrarYok()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(5, ct);

        var first = await GetAsync($"/v1/wallets/{wallet}/movements?size=2", ct);
        var firstIds = first.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("movementId").GetInt64()).ToArray();

        firstIds.Length.ShouldBe(2);

        var cursor = first.GetProperty("nextCursor").GetInt64();

        var second = await GetAsync($"/v1/wallets/{wallet}/movements?size=2&after={cursor}", ct);
        var secondIds = second.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("movementId").GetInt64()).ToArray();

        secondIds.Length.ShouldBe(2);
        secondIds.ShouldNotContain(firstIds[0]);
        secondIds.ShouldNotContain(firstIds[1]);
        secondIds[0].ShouldBeLessThan(firstIds[1], "cursor'dan küçük id'ler geliyor");
    }

    /// <summary>
    /// Cursor'ın asıl gerekçesi. İki sayfa arasında yeni hareket eklendiğinde
    /// müşteri gördüğü kayıtları tekrar görmüyor. Offset olsaydı ikinci sayfa
    /// kayar ve ilk sayfadaki kayıtlar tekrarlardı.
    /// </summary>
    [Fact]
    public async Task SayfalarArasindaYeniHareket_TekrarUretmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(4, ct);

        var first = await GetAsync($"/v1/wallets/{wallet}/movements?size=2", ct);
        var firstIds = first.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("movementId").GetInt64()).ToArray();
        var cursor = first.GetProperty("nextCursor").GetInt64();

        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, wallet, 999m, ct);
        }

        var second = await GetAsync($"/v1/wallets/{wallet}/movements?size=2&after={cursor}", ct);
        var secondIds = second.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("movementId").GetInt64()).ToArray();

        foreach (var id in firstIds)
        {
            secondIds.ShouldNotContain(id, "araya giren hareket sayfayı kaydırmamalı");
        }
    }

    [Fact]
    public async Task SonSayfada_NextCursorNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(2, ct);

        var page = await GetAsync($"/v1/wallets/{wallet}/movements?size=50", ct);

        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Unbounded query yok (baseline.md madde 8). Sınırın üstünde bir boyut
    /// isteyen request reddedilmiyor, sınıra çekiliyor.
    /// </summary>
    [Fact]
    public async Task BuyukSize_TavanaCekilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(1, ct);

        var page = await GetAsync($"/v1/wallets/{wallet}/movements?size=5000", ct);

        page.GetProperty("size").GetInt32().ShouldBe(100);
    }

    [Fact]
    public async Task Hareket_KovaTipiniVeIslemTipiniTasir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await WalletWithMovementsAsync(1, ct);

        var page = await GetAsync($"/v1/wallets/{wallet}/movements", ct);
        var item = page.GetProperty("items")[0];

        item.GetProperty("fundType").GetString().ShouldBe(FundType.Cash.ToText());
        item.GetProperty("type").GetString().ShouldBe("topup");
    }

    /// <summary>
    /// Sistem hesabı bu endpoint'ten görünmez — cüzdan sorgusundaki kuralın aynısı.
    /// </summary>
    [Fact]
    public async Task SistemHesabi_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync(
            $"/v1/wallets/{SystemAccounts.RevenueTry}/movements", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

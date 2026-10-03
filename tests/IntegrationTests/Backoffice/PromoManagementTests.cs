using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Backoffice;

/// <summary>
/// Promo'nun backoffice'ten yönetimi (decisions.md madde 37): işyerinin promo kabulü,
/// personel promo'su ve kampanyalar. Hepsi pazarlama rolünün işi; kampanyaları her
/// çalışan görüntüleyebiliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PromoManagementTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static string NewStaff() => $"calisan-{Guid.NewGuid():N}";

    private HttpClient AsMarketing(out string staff)
    {
        staff = NewStaff();
        return _client.AsStaff(staff, StaffRoles.Marketing);
    }

    private async Task<(Guid Account, Guid Wallet)> CustomerAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        return (account, await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct));
    }

    private async Task<Guid> MerchantAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
    }

    private static HttpRequestMessage Post(string path, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    // --- İşyerinin promo kabulü ---

    [Fact]
    public async Task PromoKabulu_PazarlamaIsaretler()
    {
        var ct = TestContext.Current.CancellationToken;
        var merchant = await MerchantAsync(ct);
        var client = AsMarketing(out _);

        var response = await client.PutAsJsonAsync($"/v1/accounts/{merchant}/accepts-promo", new { acceptsPromo = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, string.Join("\n", _factory.Errors));
        var detail = await client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{merchant}", ct);
        detail.GetProperty("acceptsPromo").GetBoolean().ShouldBeTrue();
    }

    /// <summary>İşaret işyerinin: bireysel hesapta anlamı yok.</summary>
    [Fact]
    public async Task PromoKabulu_BireyselHesapta_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (customer, _) = await CustomerAsync(ct);

        var response = await AsMarketing(out _)
            .PutAsJsonAsync($"/v1/accounts/{customer}/accepts-promo", new { acceptsPromo = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(StaffRoles.Support)]
    [InlineData(StaffRoles.Operations)]
    [InlineData(StaffRoles.Finance)]
    public async Task PromoKabulu_BaskaRol_403(string role)
    {
        var ct = TestContext.Current.CancellationToken;
        var merchant = await MerchantAsync(ct);

        var response = await _client.AsStaff(NewStaff(), role)
            .PutAsJsonAsync($"/v1/accounts/{merchant}/accepts-promo", new { acceptsPromo = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Personel promo'su ---

    /// <summary>
    /// Platform fonlu: promo_expense düşüyor, müşterinin promo kovası artıyor. Ledger'da
    /// aktör çalışan ve kimliği kimlik sağlayıcıdaki sub'ı.
    /// </summary>
    [Fact]
    public async Task PersonelPromo_PlatformFonlu_AktoruCalisan()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);
        var client = AsMarketing(out var staff);

        var response = await client.SendAsync(Post(
            $"/v1/wallets/{wallet}/promos",
            new { amount = 50m, currency = "TRY", scope = "all_businesses" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        var grantId = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("grantId").GetGuid();

        await using var db = postgres.CreateContext();
        var grant = await db.PromoGrants.SingleAsync(g => g.Id == grantId, ct);
        grant.Funder.ShouldBe(PromoFunder.Platform);
        grant.Scope.ShouldBe(PromoScope.AllBusinesses);

        var tx = await db.LedgerTransactions.Include(t => t.Entries).SingleAsync(t => t.Id == grant.LedgerTransactionId, ct);
        tx.ActorType.ShouldBe(ActorType.Employee);
        tx.ActorId.ShouldBe(staff);
        tx.Entries.ShouldContain(e => e.LedgerAccountId == SystemAccounts.PromoExpenseTry && e.Amount == -50m);
        tx.Entries.ShouldContain(e => e.LedgerAccountId == wallet && e.Amount == 50m && e.FundType == FundType.Promo);
    }

    [Fact]
    public async Task PersonelPromo_TekrarYeniPartiAcmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);
        var client = AsMarketing(out _);
        var key = Guid.NewGuid().ToString();
        var body = new { amount = 20m, currency = "TRY", scope = "all_businesses" };

        (await client.SendAsync(Post($"/v1/wallets/{wallet}/promos", body, key), ct)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        var second = await client.SendAsync(Post($"/v1/wallets/{wallet}/promos", body, key), ct);

        (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("replayed").GetBoolean().ShouldBeTrue();

        await using var db = postgres.CreateContext();
        (await db.PromoGrants.CountAsync(g => g.LedgerAccountId == wallet, ct)).ShouldBe(1);
    }

    /// <summary>
    /// Tek seferlik tavan ayarda (TRY için 500). Üstü reddediliyor; büyük tutar
    /// kampanyayla veriliyor.
    /// </summary>
    [Fact]
    public async Task PersonelPromo_TavaniAsarsa_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);

        var response = await AsMarketing(out _).SendAsync(Post(
            $"/v1/wallets/{wallet}/promos",
            new { amount = 500.01m, currency = "TRY", scope = "all_businesses" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Kapsamdaki her hesap işyeri olmalı; bireysel hesapta promo harcanmıyor.</summary>
    [Fact]
    public async Task PersonelPromo_KapsamdaIsyeriOlmayanHesap_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);
        var (other, _) = await CustomerAsync(ct);

        var response = await AsMarketing(out _).SendAsync(Post(
            $"/v1/wallets/{wallet}/promos",
            new { amount = 10m, currency = "TRY", scope = "selected_businesses", merchantAccountIds = new[] { other } },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PersonelPromo_SeciliIsyerleri_PartiyeYaziliyor()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);
        var merchant = await MerchantAsync(ct);

        var response = await AsMarketing(out _).SendAsync(Post(
            $"/v1/wallets/{wallet}/promos",
            new { amount = 10m, currency = "TRY", scope = "selected_businesses", merchantAccountIds = new[] { merchant } },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var promos = await _client.GetFromJsonAsync<JsonElement>($"/v1/wallets/{wallet}/promos", ct);
        promos.GetProperty("items")[0].GetProperty("merchantAccountIds")[0].GetGuid().ShouldBe(merchant);
    }

    [Fact]
    public async Task PersonelPromo_AnahtarYok_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);

        var response = await AsMarketing(out _).SendAsync(Post(
            $"/v1/wallets/{wallet}/promos", new { amount = 10m, currency = "TRY", scope = "all_businesses" }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PersonelPromo_OperasyonRolu_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, wallet) = await CustomerAsync(ct);

        var response = await _client.AsStaff(NewStaff(), StaffRoles.Operations).SendAsync(Post(
            $"/v1/wallets/{wallet}/promos",
            new { amount = 10m, currency = "TRY", scope = "all_businesses" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Kampanyalar ---

    private static object DailyCampaign(string name) => new
    {
        name,
        rule = "daily_payment_total",
        thresholdAmount = 1000m,
        rewardType = "fixed",
        rewardAmount = 25m,
        currency = "TRY",
        grantScope = "all_businesses",
        grantValidForDays = 30,
        budget = 10000m,
        dailyCapPerAccount = 25m,
        totalCapPerAccount = 100m,
        startsAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    [Fact]
    public async Task Kampanya_AcilirListelenirBitirilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = AsMarketing(out var staff);
        var name = $"Deneme {Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync("/v1/promo-campaigns", DailyCampaign(name), ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("campaignId").GetGuid();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/v1/promo-campaigns/{id}", ct);
        detail.GetProperty("name").GetString().ShouldBe(name);
        detail.GetProperty("createdBy").GetString().ShouldBe(staff);
        detail.GetProperty("endsAt").ValueKind.ShouldBe(JsonValueKind.Null);

        var list = await _client.AsStaff(NewStaff(), StaffRoles.Support)
            .GetFromJsonAsync<JsonElement>("/v1/promo-campaigns?size=100", ct);
        list.GetProperty("items").EnumerateArray().ShouldContain(c => c.GetProperty("campaignId").GetGuid() == id);

        var ended = await AsMarketing(out var ender).PostAsync($"/v1/promo-campaigns/{id}/end", null, ct);
        ended.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterEnd = await ended.Content.ReadFromJsonAsync<JsonElement>(ct);
        afterEnd.GetProperty("endsAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        afterEnd.GetProperty("endedBy").GetString().ShouldBe(ender);

        await using var db = postgres.CreateContext();
        var campaign = await db.PromoCampaigns.SingleAsync(c => c.Id == id, ct);
        campaign.IsActiveAt(DateTimeOffset.UtcNow.AddDays(2)).ShouldBeFalse();
    }

    /// <summary>Kuralın parçaları uyuşmuyor: günlük eşik kuralında eşik yok.</summary>
    [Fact]
    public async Task Kampanya_KuralUyusmuyor_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await AsMarketing(out _).PostAsJsonAsync("/v1/promo-campaigns", new
        {
            name = "Eşiksiz",
            rule = "daily_payment_total",
            rewardType = "fixed",
            rewardAmount = 25m,
            currency = "TRY",
            grantScope = "all_businesses",
            budget = 10000m,
            dailyCapPerAccount = 25m,
            totalCapPerAccount = 100m,
            startsAt = DateTimeOffset.UtcNow
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kampanya_DestekAcamaz_MusteriGoremez()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _client.AsStaff(NewStaff(), StaffRoles.Support).PostAsJsonAsync("/v1/promo-campaigns", DailyCampaign("x"), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await _client.As($"test-{Guid.NewGuid():N}").GetAsync("/v1/promo-campaigns", ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

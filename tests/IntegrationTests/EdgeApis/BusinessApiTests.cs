using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// business-api: işyerinin sistem entegrasyonu, arkasında gerçek wallet-api ve
/// withdrawal-orchestrator ile. Çağıran işyerinin sunucusu; token'daki kimlik
/// entegrasyon istemcisinin servis hesabı ve işyeri hesabının kullanıcısı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BusinessApiTests(PostgresFixture postgres, OrchestratorFixture orchestratorDb)
    : IAsyncLifetime
{
    private const string Iban = "TR330006100519786457841326";

    private WalletApiFactory _walletApi = null!;
    private WithdrawalOrchestratorApiFactory _orchestrator = null!;
    private BusinessApiFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _merchant;
    private Guid _merchantWallet;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _merchant = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            _merchantWallet = await LedgerSeeder.CreateWalletAsync(db, _merchant, "Kasa", ct);
            await LedgerSeeder.FundAsync(db, _merchantWallet, 1_000m, ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _orchestrator = new WithdrawalOrchestratorApiFactory(orchestratorDb);
        _factory = new BusinessApiFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            withdrawalOrchestrator: new PassthroughHandler(_orchestrator.CreateClient()));
        _client = _factory.CreateClient().AsIntegrationOf(_merchant);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _orchestrator.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private async Task<Guid> CustomerWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var customer = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        return await LedgerSeeder.CreateWalletAsync(db, customer, "Ana", ct);
    }

    private static HttpRequestMessage Post(string path, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    [Fact]
    public async Task Hesaplar_IsyerininHesabiniVeCuzdaniniDoner()
    {
        var ct = TestContext.Current.CancellationToken;

        var mine = await ReadAsync(await _client.GetAsync("/v1/accounts", ct), ct);
        mine.GetProperty("items")[0].GetProperty("accountId").GetGuid().ShouldBe(_merchant);
        mine.GetProperty("items")[0].GetProperty("type").GetString().ShouldBe("Business");

        var detail = await _client.GetAsync($"/v1/accounts/{_merchant}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        (await ReadAsync(detail, ct)).GetProperty("wallets")[0].GetProperty("walletId").GetGuid()
            .ShouldBe(_merchantWallet);
    }

    [Fact]
    public async Task CuzdanVeHareketler_WalletApidenGelir()
    {
        var ct = TestContext.Current.CancellationToken;

        var wallet = await ReadAsync(await _client.GetAsync($"/v1/wallets/{_merchantWallet}", ct), ct);
        wallet.GetProperty("balance").GetDecimal().ShouldBe(1_000m);

        var movements = await ReadAsync(
            await _client.GetAsync($"/v1/wallets/{_merchantWallet}/movements?size=1", ct), ct);
        movements.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    /// <summary>İşyerinin müşterisine ödemesi (B2P). Tekrar yeni transfer yapmıyor.</summary>
    [Fact]
    public async Task Transfer_MusteriyeOdeme_TekrarYeniTransferYapmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerWalletAsync(ct);
        var key = Guid.NewGuid().ToString();
        var body = new { fromWalletId = _merchantWallet, toWalletId = customer, amount = 40m, currency = "TRY", type = "B2P" };

        var first = await _client.SendAsync(Post("/v1/transfers", body, key), ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));

        var second = await _client.SendAsync(Post("/v1/transfers", body, key), ct);
        (await ReadAsync(second, ct)).GetProperty("replayed").GetBoolean().ShouldBeTrue();

        await using var db = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(db, customer, ct)).ShouldBe(40m);
    }

    /// <summary>
    /// İşyeri kendi müşterisine promo veriyor; promo işyerinin cash kovasından çıkıyor.
    /// Location YOK: parti listesi müşterinin cüzdanında ve işyeri onu göremiyor.
    /// </summary>
    [Fact]
    public async Task Promo_MusteriyeVerilir_IsyerininNakdiDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerWalletAsync(ct);

        var response = await _client.SendAsync(Post(
            "/v1/promos",
            new { funderWalletId = _merchantWallet, walletId = customer, amount = 25m, currency = "TRY" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        response.Headers.Location.ShouldBeNull();
        (await ReadAsync(response, ct)).GetProperty("grantId").GetGuid().ShouldNotBe(Guid.Empty);

        await using var db = postgres.CreateContext();
        (await db.LedgerBalances
                .Where(b => b.LedgerAccountId == _merchantWallet && b.FundType == FundType.Cash)
                .Select(b => b.Balance)
                .SingleAsync(ct))
            .ShouldBe(975m);
    }

    /// <summary>Başka bir işyerinin kasasından promo verilemiyor: wallet-api sahipliği reddediyor.</summary>
    [Fact]
    public async Task Promo_BaskaIsyerininKasasindan_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = await CustomerWalletAsync(ct);

        Guid otherWallet;
        await using (var db = postgres.CreateContext())
        {
            var other = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            otherWallet = await LedgerSeeder.CreateWalletAsync(db, other, "Kasa", ct);
            await LedgerSeeder.FundAsync(db, otherWallet, 500m, ct);
        }

        var response = await _client.SendAsync(Post(
            "/v1/promos",
            new { funderWalletId = otherWallet, walletId = customer, amount = 25m, currency = "TRY" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// İşyerinin kasasından IBAN'a çekim. Bakiye YOK: bıraktığı komutu aynı şemada
    /// broker'la koşan zincir testinin relay'i yayınlayabiliyor, bakiyesiz cüzdanda
    /// düşme reddediliyor ve ledger'a hiçbir şey yazılmıyor.
    /// </summary>
    [Fact]
    public async Task Cekim_202VeLocationBusinessApiyiGosterir()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid emptyWallet;
        await using (var db = postgres.CreateContext())
        {
            emptyWallet = await LedgerSeeder.CreateWalletAsync(db, _merchant, "Boş kasa", ct);
        }

        var response = await _client.SendAsync(Post(
            "/v1/withdrawals",
            new { walletId = emptyWallet, amount = 100m, currency = "TRY", destinationIban = Iban },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, string.Join("\n", _walletApi.Errors));
        var withdrawalId = (await ReadAsync(response, ct)).GetProperty("withdrawalId").GetGuid();

        var location = response.Headers.Location.ShouldNotBeNull();
        location.Host.ShouldBe(_client.BaseAddress!.Host);
        location.AbsolutePath.ShouldBe($"/v1/withdrawals/{withdrawalId}");

        (await _client.GetAsync(location, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

/// <summary>
/// Her ön API yalnızca kendisi için verilmiş token'ı kabul ediyor. İki ön API de aynı
/// kimlik sağlayıcıya güveniyor; ayrımı token'ın hedef kitlesi yapıyor.
/// </summary>
public sealed class EdgeAudienceTests
{
    [Fact]
    public async Task MobilUygulamaninTokeni_BusinessApide401()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new BusinessApiFactory();
        using var client = factory.CreateClient().As("test-mobil");

        var response = await client.GetAsync("/v1/accounts", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Müşteriye hizmet eden ön API çalışanların realm'ini tanımıyor: imzası geçerli ve
    /// hedef kitlesi doğru olsa da çalışanın token'ı reddediliyor.
    /// </summary>
    [Fact]
    public async Task CalisaninTokeni_MobilOnApide401()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestTokens.For(
                "test-calisan",
                audiences: [TestTokens.PersonalMobileAudience, TestTokens.InternalAudience],
                issuer: TestTokens.StaffIssuer));

        var response = await client.GetAsync("/v1/accounts", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IsyerininTokeni_MobilOnApide401()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory();
        using var client = factory.CreateClient().AsIntegration("test-isyeri");

        var response = await client.GetAsync("/v1/accounts", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

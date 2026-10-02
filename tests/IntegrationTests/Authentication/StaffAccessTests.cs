using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.IdentityModel.Tokens;

namespace HiWallet.IntegrationTests.Authentication;

/// <summary>
/// Çalışanın token'ı çalışanların Keycloak'ından geliyor ve iç servisler onu da doğruluyor.
/// Çalışan <c>customer.view</c> izniyle her müşterinin kaydını görüntüleyebiliyor;
/// müşterinin para hareketi başlatan uçlarını kullanamıyor. Çalışanın yazma işleri kendi
/// uçlarında ve kendi iznine bağlı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaffAccessTests(PostgresFixture postgres, OrchestratorFixture orchestratorDb) : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private WithdrawalOrchestratorApiFactory _orchestrator = null!;
    private HttpClient _wallet = null!;
    private HttpClient _withdrawals = null!;
    private Guid _account;
    private Guid _walletId;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _walletId = await LedgerSeeder.CreateWalletAsync(db, _account, "Ana", ct);
            await LedgerSeeder.FundAsync(db, _walletId, 100m, ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _orchestrator = new WithdrawalOrchestratorApiFactory(orchestratorDb);
        _wallet = _walletApi.CreateClient();
        _withdrawals = _orchestrator.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _wallet.Dispose();
        _withdrawals.Dispose();
        await _orchestrator.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static string NewStaff() => $"calisan-{Guid.NewGuid():N}";

    [Fact]
    public async Task GoruntulemeIzni_MusterininKaydiniGoruntuler()
    {
        var ct = TestContext.Current.CancellationToken;
        _wallet.AsStaff(NewStaff(), StaffPermissions.CustomerView);

        foreach (var path in new[]
                 {
                     $"/v1/accounts/{_account}",
                     $"/v1/wallets/{_walletId}",
                     $"/v1/wallets/{_walletId}/movements",
                     $"/v1/wallets/{_walletId}/promos"
                 })
        {
            var response = await _wallet.GetAsync(path, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path}\n{string.Join("\n", _walletApi.Errors)}");
        }
    }

    /// <summary>
    /// Görüntüleme izni olmayan çalışan müşteri kaydını görmüyor; başka izinleri olsa da.
    /// Rolün adı değil içindeki izin sayılıyor.
    /// </summary>
    [Fact]
    public async Task GoruntulemeIzniYok_403()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _wallet.AsStaff(NewStaff(), StaffPermissions.PromoGrant, StaffPermissions.CampaignManage)
            .GetAsync($"/v1/wallets/{_walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>İzni olmayan çalışan hiçbir şey görmüyor: realm'de kullanıcı olmak yetki değil.</summary>
    [Fact]
    public async Task IzinsizCalisan_403()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _wallet.AsStaff(NewStaff()).GetAsync($"/v1/wallets/{_walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Müşterinin transfer ucu çalışana kapalı: izni olsa da müşterinin cüzdanından
    /// para gönderemiyor ve ledger değişmiyor.
    /// </summary>
    [Fact]
    public async Task CalisanMusterininUcundanTransferYapamaz_403()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid other;
        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            other = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId = _walletId, toWalletId = other, amount = 30m, currency = "TRY", type = "P2P" })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _wallet.AsStaff(NewStaff(), TestStaff.Operations).SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var check = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(check, _walletId, ct)).ShouldBe(100m);
    }

    /// <summary>Çalışan müşteri yoluyla hesap açamıyor ve "benim hesaplarım" listesi ona kapalı.</summary>
    [Fact]
    public async Task CalisanMusteriHesabiAcamaz_403()
    {
        var ct = TestContext.Current.CancellationToken;
        _wallet.AsStaff(NewStaff(), TestStaff.Support);

        (await _wallet.PostAsJsonAsync("/v1/accounts", new { type = "Person" }, ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await _wallet.GetAsync("/v1/accounts", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Çalışanların realm'inin adresini taşıyan ama başka anahtarla imzalanmış token.</summary>
    [Fact]
    public async Task BaskaAnahtarlaImzalanmisCalisanTokeni_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var forged = TestTokens.For(
            NewStaff(),
            signingKey: new RsaSecurityKey(RSA.Create(2048)),
            audiences: [TestTokens.InternalAudience],
            issuer: TestTokens.StaffIssuer,
            roles: TestStaff.Support);
        _wallet.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await _wallet.GetAsync($"/v1/wallets/{_walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TanimadigiIssuer_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = TestTokens.For(
            NewStaff(),
            audiences: [TestTokens.InternalAudience],
            issuer: "https://idp.hiwallet.test/realms/baska",
            roles: TestStaff.Support);
        _wallet.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _wallet.GetAsync($"/v1/wallets/{_walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Çalışan müşterinin çekimini görüntülüyor ama müşteri adına çekim başlatamıyor:
    /// çekim başlatan uç müşterinin.
    /// </summary>
    [Fact]
    public async Task CalisanCekimiGoruntuler_BaslatamazO()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = Guid.NewGuid();

        HttpRequestMessage Start() => new(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId = Guid.NewGuid(),
                amount = 100m,
                currency = "TRY",
                destinationIban = "TR330006100519786457841326"
            }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var started = await _withdrawals.As(TestTokens.SubjectOf(accountId)).SendAsync(Start(), ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var withdrawalId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        _withdrawals.AsStaff(NewStaff(), TestStaff.Support);

        (await _withdrawals.GetAsync($"/v1/withdrawals/{withdrawalId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _withdrawals.SendAsync(Start(), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

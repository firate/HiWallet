using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// personal-web-bff: bireysel web uygulamasının BFF'i, arkasında gerçek wallet-api ve
/// withdrawal-orchestrator ile. Tarayıcı yalnızca oturum cookie'si taşıyor; iç servise
/// giden token oturumdan okunuyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersonalWebBffTests(PostgresFixture postgres, OrchestratorFixture orchestratorDb)
    : IAsyncLifetime
{
    private const string Iban = "TR330006100519786457841326";

    private WalletApiFactory _walletApi = null!;
    private WithdrawalOrchestratorApiFactory _orchestrator = null!;
    private PersonalWebBffFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _customer;
    private Guid _wallet;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _customer = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _wallet = await LedgerSeeder.CreateWalletAsync(db, _customer, "Ana", ct);
            await LedgerSeeder.FundAsync(db, _wallet, 500m, ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _orchestrator = new WithdrawalOrchestratorApiFactory(orchestratorDb);
        _factory = new PersonalWebBffFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            withdrawalOrchestrator: new PassthroughHandler(_orchestrator.CreateClient()));
        _client = _factory.CreateClient().SignedInAsOwnerOf(_customer).WithCsrfHeader();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _orchestrator.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static HttpRequestMessage Post(string path, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ct);

    [Fact]
    public async Task Hesaplar_OturumunTokeniyleWalletApiden()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/v1/accounts", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        (await ReadAsync(response, ct)).GetProperty("items")[0].GetProperty("accountId").GetGuid().ShouldBe(_customer);
    }

    [Fact]
    public async Task Transfer_TekrarYeniTransferYapmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid friend;
        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            friend = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        }

        var key = Guid.NewGuid().ToString();
        var body = new { fromWalletId = _wallet, toWalletId = friend, amount = 30m, currency = "TRY", type = "P2P" };

        var first = await _client.SendAsync(Post("/v1/transfers", body, key), ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));

        var second = await _client.SendAsync(Post("/v1/transfers", body, key), ct);
        (await ReadAsync(second, ct)).GetProperty("replayed").GetBoolean().ShouldBeTrue();

        await using var check = postgres.CreateContext();
        (await LedgerSeeder.BalanceAsync(check, friend, ct)).ShouldBe(30m);
    }

    /// <summary>
    /// Bakiye YOK: bıraktığı komutu aynı şemada broker'la koşan zincir testinin relay'i
    /// yayınlayabiliyor, bakiyesiz cüzdanda düşme reddediliyor ve ledger'a hiçbir şey
    /// yazılmıyor.
    /// </summary>
    [Fact]
    public async Task Cekim_202VeLocationBffiGosterir()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid emptyWallet;
        await using (var db = postgres.CreateContext())
        {
            emptyWallet = await LedgerSeeder.CreateWalletAsync(db, _customer, "Boş", ct);
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

    [Fact]
    public async Task Kullanici_OturumunKimligi()
    {
        var ct = TestContext.Current.CancellationToken;

        var user = await ReadAsync(await _client.GetAsync("/bff/user", ct), ct);

        user.GetProperty("subject").GetString().ShouldBe(TestTokens.SubjectOf(_customer));
        user.GetProperty("name").GetString().ShouldBe("Deneme Kullanıcı");
    }
}

/// <summary>
/// Oturumun kendisi: tarayıcıya dönen cevaplar ve CSRF başlığı. İç servise ulaşmıyor.
/// </summary>
public sealed class PersonalWebBffSessionTests
{
    /// <summary>
    /// API çağrısı Keycloak'a YÖNLENDİRİLMİYOR, 401 alıyor: fetch yönlendirmeyi
    /// takip edip giriş sayfasının HTML'ini JSON diye okumaya çalışırdı. Girişi uygulama
    /// başlatıyor.
    /// </summary>
    [Fact]
    public async Task OturumYok_401YonlendirmeYok()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalWebBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .WithCsrfHeader();

        foreach (var path in new[] { "/v1/accounts", "/bff/user" })
        {
            var response = await client.GetAsync(path, ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            response.Headers.Location.ShouldBeNull(path);
        }
    }

    /// <summary>
    /// Giriş Keycloak'ın giriş sayfasına gidiyor: kod akışı, PKCE ve BFF'in kendi
    /// istemcisi. Dönüş adresi BFF'in kendisi; token'ı tarayıcı değil BFF alıyor.
    /// </summary>
    [Fact]
    public async Task Giris_KeycloakaPkceIleYonlendiriyor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalWebBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/bff/login?returnUrl=/cuzdanlar", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location.ShouldNotBeNull();
        location.GetLeftPart(UriPartial.Path).ShouldBe(factory.AuthorizationEndpoint);

        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe(factory.ClientId);
        query["response_type"].ShouldBe("code");
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"].ShouldBe("http://localhost/signin-oidc");
    }

    /// <summary>
    /// Kaydı biten müşteri girişe e-postası dolu gidiyor: Keycloak'a <c>login_hint</c>.
    /// </summary>
    [Fact]
    public async Task Giris_EpostaIpucuKeycloakaGider()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalWebBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/bff/login?returnUrl=/&loginHint=musteri%40ornek.com", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var query = HttpUtility.ParseQueryString(response.Headers.Location.ShouldNotBeNull().Query);
        query["login_hint"].ShouldBe("musteri@ornek.com");
    }

    /// <summary>
    /// Başlıksız API isteği reddediliyor. Cookie aynı sitedeki başka bir alt alan adından
    /// gelen isteğe de ekleniyor; o sayfa bu başlığı ekleyemiyor, çünkü başlık tarayıcıda
    /// CORS ön kontrolünü tetikliyor ve BFF buna izin vermiyor.
    /// </summary>
    [Fact]
    public async Task CsrfBasligiYok_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalWebBffFactory();
        using var client = factory.CreateClient().SignedInAs("test-csrf");

        var response = await client.GetAsync("/v1/accounts", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Bilinmeyen API yolu 404; uygulamanın sayfası (index.html) DÖNMÜYOR. Dönseydi
    /// yanlış yazılmış bir uç 200 ve HTML ile cevap verirdi.
    /// </summary>
    [Fact]
    public async Task BilinmeyenApiYolu_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PersonalWebBffFactory();
        using var client = factory.CreateClient().SignedInAs("test-yol").WithCsrfHeader();

        var response = await client.GetAsync("/v1/olmayan-uc", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Uygulamanın sayfası kimliksiz geliyor: kök adres de uygulamanın kendi yolları da.
    /// Oturum açmayan kullanıcı giriş düğmesini bu sayfada görüyor.
    /// </summary>
    [Fact]
    public async Task UygulamaninSayfasi_KimliksizGelir()
    {
        var ct = TestContext.Current.CancellationToken;
        var webRoot = Directory.CreateTempSubdirectory("hiwallet-web-");

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "index.html"), "<!doctype html><title>HiWallet</title>", ct);
            await using var factory = new PersonalWebBffFactory();
            await using var withPages = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot.FullName));
            using var client = withPages.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            foreach (var path in new[] { "/", "/cuzdanlar/abc" })
            {
                var response = await client.GetAsync(path, ct);

                response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
                response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", path);
            }
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }
}

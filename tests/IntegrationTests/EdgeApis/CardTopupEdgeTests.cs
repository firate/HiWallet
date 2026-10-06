using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// Kartla yükleme bireysel müşterinin iki ön API'sinden: arkasında gerçek kart yüklemesi
/// servisi, wallet-api ve sahte sağlayıcı. Ön API isteği iletiyor, reddi aynen aktarıyor.
/// Tarayıcının dönüş adresini BFF kendi adresinden kuruyor; mobil uygulama kendisi veriyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardTopupEdgeTests(PostgresFixture postgres, CardTopupFixture cards) : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private StripeFakeFactory _provider = null!;
    private CardTopupApiFactory _cardTopup = null!;
    private PersonalWebBffFactory _bff = null!;
    private PersonalMobileApiFactory _mobile = null!;
    private Guid _account;
    private Guid _wallet;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Unverified);
            _wallet = await LedgerSeeder.CreateWalletAsync(db, _account, "Ana", ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _provider = new StripeFakeFactory(new HttpClient(new UnreachableHandler()));
        _cardTopup = new CardTopupApiFactory(
            cards,
            new PassthroughHandler(_walletApi.CreateClient()),
            new PassthroughHandler(_provider.CreateClient()));

        _bff = new PersonalWebBffFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            cardTopup: new PassthroughHandler(_cardTopup.CreateClient()));

        _mobile = new PersonalMobileApiFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            cardTopup: new PassthroughHandler(_cardTopup.CreateClient()));
    }

    public async ValueTask DisposeAsync()
    {
        await _mobile.DisposeAsync();
        await _bff.DisposeAsync();
        await _cardTopup.DisposeAsync();
        await _provider.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static HttpRequestMessage Post(object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/card-topups") { Content = JsonContent.Create(body) };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ct);

    /// <summary>
    /// Tarayıcı yalnızca tutarı veriyor; dönüş adresi BFF'in kendi adresindeki sayfa. Ödeme
    /// sayfası müşteriyi oraya yüklemenin kimliğiyle yolluyor.
    /// </summary>
    [Fact]
    public async Task Bff_202_DonusAdresiBffinSayfasi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _bff.CreateClient().SignedInAsOwnerOf(_account).WithCsrfHeader();

        var response = await client.SendAsync(
            Post(new { walletId = _wallet, amount = 250m, currency = "TRY" }, Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, string.Join("\n", _walletApi.Errors));

        var body = await ReadAsync(response, ct);
        var id = body.GetProperty("cardTopupId").GetGuid();
        var paymentUrl = body.GetProperty("paymentUrl").GetString().ShouldNotBeNull();

        var location = response.Headers.Location.ShouldNotBeNull();
        location.Host.ShouldBe(client.BaseAddress!.Host);
        location.AbsolutePath.ShouldBe($"/v1/card-topups/{id}");

        var status = await client.GetAsync(location, ct);
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(status, ct)).GetProperty("state").GetString().ShouldBe("pending");

        var decided = await _provider.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .PostAsync(new Uri(paymentUrl).AbsolutePath, new FormUrlEncodedContent([new("action", "cancel")]), ct);

        decided.Headers.Location.ShouldBe(new Uri($"{client.BaseAddress.GetLeftPart(UriPartial.Authority)}/kart-yukleme?cardTopupId={id}"));
    }

    /// <summary>Limit yetmiyor: wallet'ın reddi iki iç servisten geçip aynen geliyor.</summary>
    [Fact]
    public async Task Bff_LimitYetmiyor_422KuralAdi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _bff.CreateClient().SignedInAsOwnerOf(_account).WithCsrfHeader();

        var response = await client.SendAsync(
            Post(new { walletId = _wallet, amount = 6_000m, currency = "TRY" }, Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadAsync(response, ct)).GetProperty("rule").GetString().ShouldBe("card_topup_limit");
    }

    [Fact]
    public async Task Bff_AnahtarYok_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _bff.CreateClient().SignedInAsOwnerOf(_account).WithCsrfHeader();

        var response = await client.SendAsync(Post(new { walletId = _wallet, amount = 10m, currency = "TRY" }, null), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Mobil uygulama dönüş adresini kendisi veriyor.</summary>
    [Fact]
    public async Task Mobil_202_UygulamaninDonusAdresi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _mobile.CreateClient().AsOwnerOf(_account);

        var response = await client.SendAsync(Post(
            new { walletId = _wallet, amount = 100m, currency = "TRY", returnUrl = "https://app.hiwallet.test/kart" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var id = (await ReadAsync(response, ct)).GetProperty("cardTopupId").GetGuid();

        await using var db = cards.CreateContext();
        (await db.CardTopups.SingleAsync(c => c.Id == id, ct)).ReturnUrl.ShouldBe("https://app.hiwallet.test/kart");

        (await client.GetAsync($"/v1/card-topups/{id}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Kart yüklemesi servisine ulaşılamıyor: 503, iç adres dışarı çıkmıyor.</summary>
    [Fact]
    public async Task Mobil_ServisYok_503()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var mobile = new PersonalMobileApiFactory(walletApi: new PassthroughHandler(_walletApi.CreateClient()));
        using var client = mobile.CreateClient().AsOwnerOf(_account);

        var response = await client.SendAsync(Post(
            new { walletId = _wallet, amount = 100m, currency = "TRY", returnUrl = "https://app.hiwallet.test/kart" },
            Guid.NewGuid().ToString()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain("card-topup");
    }
}

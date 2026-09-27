using System.Net;
using System.Net.Http.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Rate limiting dışarıdan istek alan her serviste var (baseline.md madde 7). Bu dosya
/// müşteri trafiğini karşılayan ön API'yi ve bankanın callback'lerini alan
/// <c>bank-webhook</c>'u sınıyor.
///
/// Kova küçük kuruluyor ve dakikada bir token yenileniyor: test süresince dolmadığı
/// için kaçıncı request'in reddedileceği kesin. Request'lerin geçersiz olması sorun
/// değil — limiter controller'dan önce koşuyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(BankFixture bankDb)
{
    private const int Burst = 3;

    /// <summary>
    /// Müşteri başına sınır ön API'de: istemciyi tanıyan o. İç servisler yalnızca ön
    /// API'lerin adresini görüyor; orada IP'ye göre bölünen bir kova bütün müşterileri
    /// tek kovaya koyardı.
    /// </summary>
    [Fact]
    public async Task OnApi_SinirAsilinca_RetryAfterIle429Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory(rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        for (var i = 0; i < Burst; i++)
        {
            var allowed = await client.GetAsync($"/v1/withdrawals/{Guid.NewGuid()}", ct);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"{i + 1}. request kovanın içinde");
        }

        var limited = await client.GetAsync($"/v1/withdrawals/{Guid.NewGuid()}", ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull("Retry-After yoksa client hemen tekrar dener");
        limited.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>
    /// Kova kimlik başına: IP paylaşan müşteriler (mobil operatör, kurumsal NAT)
    /// birbirinin limitini yemiyor. Kimliksiz istek IP'nin kovasına düşüyor.
    /// </summary>
    [Fact]
    public async Task OnApi_KovaKimlikBasina()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory(rateLimitBurst: Burst);
        using var first = factory.CreateClient().As("test-birinci");
        using var second = factory.CreateClient().As("test-ikinci");

        for (var i = 0; i < Burst; i++)
        {
            await first.GetAsync($"/v1/withdrawals/{Guid.NewGuid()}", ct);
        }

        (await first.GetAsync($"/v1/withdrawals/{Guid.NewGuid()}", ct)).StatusCode
            .ShouldBe(HttpStatusCode.TooManyRequests);

        (await second.GetAsync($"/v1/withdrawals/{Guid.NewGuid()}", ct)).StatusCode
            .ShouldNotBe(HttpStatusCode.TooManyRequests, "başka kimliğin kovası dolu olmamalı");
    }

    /// <summary>
    /// İşyerinin entegrasyonunda da kova istemci başına: bir işyerinin patlaması
    /// diğerinin entegrasyonunu durdurmuyor.
    /// </summary>
    [Fact]
    public async Task BusinessApi_KovaIstemciBasina()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new BusinessApiFactory(rateLimitBurst: Burst);
        using var first = factory.CreateClient().AsIntegration("test-isyeri-bir");
        using var second = factory.CreateClient().AsIntegration("test-isyeri-iki");

        for (var i = 0; i < Burst; i++)
        {
            await first.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);
        }

        (await first.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct)).StatusCode
            .ShouldBe(HttpStatusCode.TooManyRequests);

        (await second.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct)).StatusCode
            .ShouldNotBe(HttpStatusCode.TooManyRequests, "başka işyerinin kovası dolu olmamalı");
    }

    /// <summary>
    /// Çekim başlatmanın kendi, daha dar kovası var: dışarıya para çıkarıyor. Kova
    /// ayrı olduğu için çekim sınırına takılan müşteri bakiyesini görmeye devam ediyor.
    /// </summary>
    [Fact]
    public async Task OnApi_CekimBaslatmaKendiKovasini_Kullanir()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory(rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        for (var i = 0; i < Burst; i++)
        {
            await client.PostAsJsonAsync("/v1/withdrawals", new { }, ct);
        }

        var limited = await client.PostAsJsonAsync("/v1/withdrawals", new { }, ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var other = await client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);
        other.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, "genel kova dolu olmamalı");
    }

    /// <summary>
    /// Probe'un limite takılması sağlıklı bir servisi trafikten çektirir.
    /// </summary>
    [Fact]
    public async Task OnApi_HealthCheck_RateLimitEdilmez()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory(rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        for (var i = 0; i < Burst * 5; i++)
        {
            var response = await client.GetAsync("/health/live", ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{i + 1}. probe limite takıldı");
        }
    }

    /// <summary>
    /// Bankanın callback endpoint'i her request'te gövdenin tamamı üzerinde HMAC
    /// hesaplıyor. İmzası geçersiz request'ler de o hesabı yaptırıyor, sınır bu
    /// yüzden imzadan önce.
    /// </summary>
    [Fact]
    public async Task BankaCallback_SinirAsilinca_429Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new BankWebhookFactory(bankDb, rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        var path = $"/v1/webhooks/bank/{TestBankSecrets.Bank}";

        for (var i = 0; i < Burst; i++)
        {
            var allowed = await client.PostAsync(path, new StringContent("{}"), ct);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "imzasız request limitin içinde 401 almalı");
        }

        var limited = await client.PostAsync(path, new StringContent("{}"), ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }

    /// <summary>
    /// Sınır banka başına: bir bankanın kovası dolunca diğerinin callback'leri
    /// etkilenmiyor. Top-up webhook'undaki sağlayıcı ayrımının aynısı.
    /// </summary>
    [Fact]
    public async Task BankaCallback_SinirBankaBasina()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new BankWebhookFactory(bankDb, rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        for (var i = 0; i <= Burst; i++)
        {
            await client.PostAsync($"/v1/webhooks/bank/{TestBankSecrets.Bank}", new StringContent("{}"), ct);
        }

        var otherBank = await client.PostAsync("/v1/webhooks/bank/baska-banka", new StringContent("{}"), ct);

        otherBank.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, "başka bankanın kovası dolu olmamalı");
    }
}

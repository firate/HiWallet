using System.Net;
using System.Net.Http.Json;
using HiWallet.IntegrationTests.Fixtures;

namespace HiWallet.IntegrationTests.Baseline;

/// <summary>
/// Rate limiting ingress'i olan her serviste var (baseline.md madde 7).
/// <c>wallet-api</c>'ninki <see cref="BaselineTests"/>'te; bu dosya
/// <c>withdrawal-orchestrator</c> ile <c>bank-webhook</c>'u sınıyor.
///
/// Kova küçük kuruluyor ve dakikada bir token yenileniyor: test süresince dolmadığı
/// için kaçıncı request'in reddedileceği kesin. Request'lerin geçersiz olması sorun
/// değil — limiter controller'dan önce koşuyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(OrchestratorFixture orchestratorDb, BankFixture bankDb)
{
    private const int Burst = 3;

    [Fact]
    public async Task Cekim_SinirAsilinca_RetryAfterIle429Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new WithdrawalOrchestratorApiFactory(orchestratorDb, rateLimitBurst: Burst);
        using var client = factory.CreateClient();

        for (var i = 0; i < Burst; i++)
        {
            var allowed = await client.PostAsJsonAsync("/v1/withdrawals", new { }, ct);
            allowed.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests, $"{i + 1}. request kovanın içinde");
        }

        var limited = await client.PostAsJsonAsync("/v1/withdrawals", new { }, ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull("Retry-After yoksa client hemen tekrar dener");
        limited.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>
    /// Probe'un limite takılması sağlıklı bir servisi trafikten çektirir.
    /// </summary>
    [Fact]
    public async Task Cekim_HealthCheck_RateLimitEdilmez()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new WithdrawalOrchestratorApiFactory(orchestratorDb, rateLimitBurst: Burst);
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

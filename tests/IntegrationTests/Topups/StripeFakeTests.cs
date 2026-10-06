using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.CardPayments;
using HiWallet.TopupWebhook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Sahte kart sağlayıcısı: ödeme açmak, müşterinin ödeme sayfasındaki kararı ve sonucun
/// <c>topup-webhook</c>'a imzalı bildirimi. Ledger'a kadar gitmiyor; burada sınanan şey
/// sağlayıcının davranışı ve inbox'a ne düştüğü.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StripeFakeTests(InboxFixture inbox) : IAsyncLifetime
{
    private const string ReturnUrl = "http://web.test/kart-yukleme";

    private TopupWebhookApiFactory _webhook = null!;
    private StripeFakeFactory _provider = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _webhook = new TopupWebhookApiFactory(inbox);
        _provider = new StripeFakeFactory(_webhook.CreateClient());

        // Ödeme sayfası 303 ile dönüş adresine gönderiyor; testte o yönlendirme izlenmiyor.
        _client = _provider.CreateClient(new() { AllowAutoRedirect = false });

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _provider.DisposeAsync();
        await _webhook.DisposeAsync();
    }

    private async Task<JsonElement> OpenAsync(Guid reference, decimal amount = 250m, TimeSpan? lasts = null)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/v1/payments", new
        {
            reference,
            amount,
            currency = "TRY",
            returnUrl = ReturnUrl,
            expiresAt = DateTimeOffset.UtcNow + (lasts ?? TimeSpan.FromMinutes(30))
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private Task<HttpResponseMessage> DecideAsync(string paymentId, string action) =>
        _client.PostAsync(
            $"/odeme/{paymentId}",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("action", action)]),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Bildirim ARKA PLANDA gidiyor: sayfa dönüş adresine hemen yönlendiriyor. Inbox'a
    /// bakmadan önce beklemek gerekiyor; gerçek sağlayıcıda da webhook kararla aynı anda gelmiyor.
    /// </summary>
    private async Task<InboxMessage> WaitForInboxAsync(Guid reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using (var db = inbox.CreateContext())
            {
                var row = await db.Inbox
                    .FromSql($"SELECT * FROM topup_inbox WHERE payload->>'reference' = {reference.ToString()}")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);

                if (row is not null) return row;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"{reference} için bildirim beş saniyede gelmedi.");
    }

    [Fact]
    public async Task OdemeAcar_SayfaAdresiniDoner()
    {
        var payment = await OpenAsync(Guid.NewGuid());

        payment.GetProperty("status").GetString().ShouldBe("requires_payment");
        payment.GetProperty("paymentUrl").GetString()
            .ShouldBe($"{StripeFakeFactory.PublicUrl}/odeme/{payment.GetProperty("id").GetString()}");
    }

    /// <summary>Ödemeyi açan, ödeme kimliğini bilmeden kendi referansıyla durumu soruyor.</summary>
    [Fact]
    public async Task ReferanslaAranir_YoksaBulunamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();
        var payment = await OpenAsync(reference);

        var found = await _client.GetFromJsonAsync<JsonElement>($"/v1/payments?reference={reference}", ct);
        found.GetProperty("id").GetString().ShouldBe(payment.GetProperty("id").GetString());

        var missing = await _client.GetAsync($"/v1/payments?reference={Guid.NewGuid()}", ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Aynı referansla ikinci istek ikinci bir ödeme açmıyor.</summary>
    [Fact]
    public async Task AyniReferans_AyniOdemeyiDoner_FarkliTutar409()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();
        var first = await OpenAsync(reference);

        var again = await _client.PostAsJsonAsync("/v1/payments", new
        {
            reference, amount = 250m, currency = "TRY", returnUrl = ReturnUrl, expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        }, ct);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()
            .ShouldBe(first.GetProperty("id").GetString());

        var conflict = await _client.PostAsJsonAsync("/v1/payments", new
        {
            reference, amount = 300m, currency = "TRY", returnUrl = ReturnUrl, expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        }, ct);

        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ode_DonusAdresineGonderir_OdendiBildirimiInboxaDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();
        var payment = await OpenAsync(reference);
        var id = payment.GetProperty("id").GetString()!;

        var page = await _client.GetStringAsync($"/odeme/{id}", ct);
        page.ShouldContain("Öde");

        var decided = await DecideAsync(id, "pay");

        decided.StatusCode.ShouldBe(HttpStatusCode.SeeOther);
        decided.Headers.Location!.ToString().ShouldBe(ReturnUrl);

        var status = await _client.GetFromJsonAsync<JsonElement>($"/v1/payments/{id}", ct);
        status.GetProperty("status").GetString().ShouldBe("succeeded");

        var row = await WaitForInboxAsync(reference, ct);
        using var message = JsonDocument.Parse(row.Payload);

        message.RootElement.GetProperty("type").GetString().ShouldBe(CardPaymentEvents.Succeeded);
        message.RootElement.GetProperty("paymentId").GetString().ShouldBe(id);
        message.RootElement.GetProperty("amount").GetDecimal().ShouldBe(250m);
    }

    [Fact]
    public async Task Vazgec_VazgecildiBildirimiInboxaDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();
        var id = (await OpenAsync(reference)).GetProperty("id").GetString()!;

        (await DecideAsync(id, "cancel")).StatusCode.ShouldBe(HttpStatusCode.SeeOther);

        var row = await WaitForInboxAsync(reference, ct);
        using var message = JsonDocument.Parse(row.Payload);

        message.RootElement.GetProperty("type").GetString().ShouldBe(CardPaymentEvents.Canceled);
    }

    /// <summary>Karar verilmiş ödeme değişmiyor: ikinci tıklama ikinci bir bildirim üretmiyor.</summary>
    [Fact]
    public async Task KararVerilmisOdeme_Degismez()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = (await OpenAsync(Guid.NewGuid())).GetProperty("id").GetString()!;

        await DecideAsync(id, "pay");
        var second = await DecideAsync(id, "cancel");

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetFromJsonAsync<JsonElement>($"/v1/payments/{id}", ct))
            .GetProperty("status").GetString().ShouldBe("succeeded");
    }

    /// <summary>
    /// Süresi dolan oturum ödeme kabul etmiyor ve bildirim göndermiyor: gerçek sağlayıcıların
    /// çoğu terk edilen ödemeyi bildirmiyor; kart yüklemesi servisi durumu soruyor.
    /// </summary>
    [Fact]
    public async Task SuresiDolanOdeme_OdenemezVeBildirimGitmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();
        var id = (await OpenAsync(reference, lasts: TimeSpan.FromSeconds(1))).GetProperty("id").GetString()!;

        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);

        (await _client.GetFromJsonAsync<JsonElement>($"/v1/payments/{id}", ct))
            .GetProperty("status").GetString().ShouldBe("expired");

        (await DecideAsync(id, "pay")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(500, ct);
        await using var db = inbox.CreateContext();
        (await db.Inbox
            .FromSql($"SELECT * FROM topup_inbox WHERE payload->>'reference' = {reference.ToString()}")
            .AnyAsync(ct)).ShouldBeFalse();
    }

    /// <summary>
    /// Sağlayıcı YANLIŞ secret ile imzalarsa webhook <c>401</c> alıyor ve inbox'a hiçbir şey
    /// yazılmıyor. Bu test olmadan diğerleri bir şey kanıtlamazdı.
    /// </summary>
    [Fact]
    public async Task YanlisSecret_InboxaHicbirSeyYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = Guid.NewGuid();

        await using var badProvider = new StripeFakeFactory(_webhook.CreateClient(), secret: "yanlis-secret");
        using var client = badProvider.CreateClient(new() { AllowAutoRedirect = false });

        var opened = await client.PostAsJsonAsync("/v1/payments", new
        {
            reference, amount = 100m, currency = "TRY", returnUrl = ReturnUrl, expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        }, ct);
        var id = (await opened.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        await client.PostAsync(
            $"/odeme/{id}", new FormUrlEncodedContent([new KeyValuePair<string, string>("action", "pay")]), ct);

        await Task.Delay(1000, ct);

        await using var db = inbox.CreateContext();
        (await db.Inbox
            .FromSql($"SELECT * FROM topup_inbox WHERE payload->>'reference' = {reference.ToString()}")
            .AnyAsync(ct)).ShouldBeFalse("geçersiz imza inbox'a satır bırakmamalı");
    }

    [Fact]
    public async Task GecersizGirdi_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/v1/payments", new
        {
            reference = Guid.Empty,
            amount = -5m,
            currency = "TL",
            returnUrl = "javascript:alert(1)",
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

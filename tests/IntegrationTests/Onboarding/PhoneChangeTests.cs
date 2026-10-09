using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// Temel doğrulamadan sonra telefon değiştirme. Numara hesabı ele geçirmenin ilk adımı:
/// yeni giriş isteniyor, kod yeni numaraya gidiyor, eski numaraya ve e-postaya haber
/// veriliyor, eski numara kayıtta kalıyor ve bankaya çekim 24 saat kapanıyor. Bir numara
/// tek müşteride; başkasının numarası olduğu ancak kod doğrulandıktan sonra söyleniyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhoneChangeTests(PostgresFixture postgres, OnboardingFixture onboardingDb) : IAsyncLifetime
{
    private WalletApiFactory _walletApi = null!;
    private OnboardingApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _factory = new OnboardingApiFactory(onboardingDb, new PassthroughHandler(_walletApi.CreateClient()));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ct);

    private static async Task<string?> RuleAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await ReadAsync(response, ct)).TryGetProperty("rule", out var rule) ? rule.GetString() : null;

    private async Task<Guid> StartAsync(HttpClient customer, string phone, CancellationToken ct)
    {
        var started = await customer.PostAsJsonAsync("/v1/me/phone-changes", new { phone }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(ct));
        return (await ReadAsync(started, ct)).GetProperty("verificationId").GetGuid();
    }

    private Task<HttpResponseMessage> ConfirmAsync(HttpClient customer, Guid verificationId, string phone, CancellationToken ct) =>
        customer.PostAsJsonAsync(
            $"/v1/me/phone-changes/{verificationId}/confirmation",
            new { code = _factory.Sms.LastCodeFor(PhoneNumbers.E164(phone)) }, ct);

    [Fact]
    public async Task Degisiklik_YeniNumarayaKodla_EskiyeVeEpostayaHaber_CekimBekler()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, accountId, email, oldPhone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        var newPhone = PhoneNumbers.New();
        using var customer = _factory.CreateClient().As(subject);

        var verificationId = await StartAsync(customer, newPhone, ct);
        var confirmed = await ConfirmAsync(customer, verificationId, newPhone, ct);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        var holdUntil = (await ReadAsync(confirmed, ct)).GetProperty("withdrawalHoldUntil").GetDateTimeOffset();
        holdUntil.ShouldBe(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));

        var status = await customer.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        status.GetProperty("phone").GetString().ShouldBe($"+90 {newPhone[1..4]} *** ** {newPhone[^2..]}");

        // Eski numaraya ve e-postaya haber: değişikliği yapan müşteri değilse görsün.
        _factory.Sms.Sent.ShouldContain(m => m.To == PhoneNumbers.E164(oldPhone) && m.Text.Contains("değişti"));
        _factory.Emails.Sent.ShouldContain(m => m.To == email && m.Body.Contains("değişti"));

        using var wallet = _walletApi.CreateClient().As(subject);
        (await wallet.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct))
            .GetProperty("withdrawalHoldUntil").GetDateTimeOffset().ShouldBe(holdUntil, TimeSpan.FromSeconds(1));

        // Eski numara silinmiyor: değişiklik kayıtta.
        await using var db = onboardingDb.CreateContext();
        var history = await db.PhoneChanges.AsNoTracking().SingleAsync(c => c.Subject == subject, ct);
        history.OldPhone!.Value.Value.ShouldBe(PhoneNumbers.E164(oldPhone));
        history.NewPhone.Value.ShouldBe(PhoneNumbers.E164(newPhone));
    }

    /// <summary>Açık kalmış bir oturumla numara değiştirilemez; parolayla yeni giriş gerekiyor.</summary>
    [Fact]
    public async Task EskiGirisle_YenidenGirisIster()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _, _, _) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        using var stale = _factory.CreateClient().AsAuthenticatedAt(subject, DateTimeOffset.UtcNow.AddHours(-1));

        var started = await stale.PostAsJsonAsync("/v1/me/phone-changes", new { phone = PhoneNumbers.New() }, ct);

        started.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RuleAsync(started, ct)).ShouldBe("reauthentication_required");
    }

    [Fact]
    public async Task BaskasininNumarasi_KodDogrulandiktanSonraReddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, _, _, takenPhone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        var (subject, _, _, ownPhone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        using var customer = _factory.CreateClient().As(subject);

        // Başlatma numaranın kimde olduğunu söylemiyor.
        var verificationId = await StartAsync(customer, takenPhone, ct);
        var confirmed = await ConfirmAsync(customer, verificationId, takenPhone, ct);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RuleAsync(confirmed, ct)).ShouldBe("phone_in_use");

        var status = await customer.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        status.GetProperty("phone").GetString().ShouldBe($"+90 {ownPhone[1..4]} *** ** {ownPhone[^2..]}");
    }

    /// <summary>Doğrulamadan sonra numara yalnızca bu akışla değişiyor; ilk doğrulamanın yolu kapalı.</summary>
    [Fact]
    public async Task DogrulamadanSonra_IlkDogrulamaYoluKapali()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _, _, _) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        using var customer = _factory.CreateClient().As(subject);

        var started = await customer.PostAsJsonAsync("/v1/me/phone-verifications", new { phone = PhoneNumbers.New() }, ct);

        started.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RuleAsync(started, ct)).ShouldBe("phone_change_required");
    }

    /// <summary>İlk doğrulamada da bir numara tek müşteride.</summary>
    [Fact]
    public async Task IlkDogrulama_BaskasininNumarasiylaReddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, _, _, takenPhone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        var (subject, _, _) = await OnboardingFlows.RegisteredAsync(_factory, ct);
        using var customer = _factory.CreateClient().As(subject);

        var started = await customer.PostAsJsonAsync("/v1/me/phone-verifications", new { phone = takenPhone }, ct);
        var verificationId = (await ReadAsync(started, ct)).GetProperty("verificationId").GetGuid();
        var confirmed = await customer.PostAsJsonAsync(
            $"/v1/me/phone-verifications/{verificationId}/confirmation",
            new { code = _factory.Sms.LastCodeFor(PhoneNumbers.E164(takenPhone)) }, ct);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await RuleAsync(confirmed, ct)).ShouldBe("phone_in_use");
    }

    [Fact]
    public async Task AyniNumara_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (subject, _, _, phone) = await OnboardingFlows.VerifiedAsync(_factory, ct);
        using var customer = _factory.CreateClient().As(subject);

        var started = await customer.PostAsJsonAsync("/v1/me/phone-changes", new { phone }, ct);

        started.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RuleAsync(started, ct)).ShouldBe("same_phone");
    }
}

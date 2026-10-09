using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Onboarding'in müşteri adımları: kayıt, telefon, kimlik ve onaylar.</summary>
public static class OnboardingFlows
{
    /// <summary>Kaydı tamamlanmış bir müşteri: kimliği, hesabı ve e-postası.</summary>
    public static async Task<(string Subject, Guid AccountId, string Email)> RegisteredAsync(
        OnboardingApiFactory factory, CancellationToken ct)
    {
        using var anonymous = factory.CreateClient();
        var email = $"musteri-{Guid.NewGuid():N}@ornek.com";

        var started = await anonymous.PostAsJsonAsync("/v1/registrations", new { email }, ct);
        var registrationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("registrationId").GetGuid();
        await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/email-verification", new { code = factory.Emails.LastCodeFor(email) }, ct);
        var completed = await anonymous.PostAsJsonAsync(
            $"/v1/registrations/{registrationId}/completion", new { password = "Guclu-Parola-1" }, ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.Created);

        var accountId = (await completed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid();
        return (factory.IdentityProvider.Users[email].Subject, accountId, email);
    }

    /// <summary>Telefonu, kimliği ve onayları tamamlamış müşteri; doğrulanan numarasıyla.</summary>
    public static async Task<(string Subject, Guid AccountId, string Email, string Phone)> VerifiedAsync(
        OnboardingApiFactory factory, CancellationToken ct, string? phone = null)
    {
        var (subject, accountId, email) = await RegisteredAsync(factory, ct);
        using var customer = factory.CreateClient().As(subject);

        phone ??= PhoneNumbers.New();
        var started = await customer.PostAsJsonAsync("/v1/me/phone-verifications", new { phone }, ct);
        var verificationId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("verificationId").GetGuid();
        var confirmed = await customer.PostAsJsonAsync(
            $"/v1/me/phone-verifications/{verificationId}/confirmation",
            new { code = factory.Sms.LastCodeFor(PhoneNumbers.E164(phone)) }, ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var identity = await customer.PutAsJsonAsync("/v1/me/identity", new
        {
            firstName = "Ayşe",
            lastName = "Yılmaz",
            nationalId = NationalIds.New(),
            birthDate = "1990-05-17"
        }, ct);
        identity.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await customer.GetFromJsonAsync<JsonElement>("/v1/me/onboarding", ct);
        var documents = status.GetProperty("documents");
        var accepted = await customer.PostAsJsonAsync("/v1/me/basic-verification", new
        {
            termsVersion = documents.GetProperty("termsVersion").GetString(),
            privacyNoticeVersion = documents.GetProperty("privacyNoticeVersion").GetString()
        }, ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (subject, accountId, email, phone);
    }
}

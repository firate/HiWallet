using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using HiWallet.Onboarding.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Onboarding.Application;

public sealed record RegistrationStarted(Guid RegistrationId, DateTimeOffset CodeExpiresAt);

/// <param name="Replayed">Kayıt bu istekten önce tamamlanmıştı.</param>
public sealed record RegistrationCompleted(Guid AccountId, string Email, bool Replayed);

/// <summary>
/// Kayıt: e-posta kodu, parola, kimlik sağlayıcıda kullanıcı ve wallet'ta hesap.
///
/// Adımlar tekrar edilebilir: kullanıcı açılıp kayda bağlanmadan kesilen bir tamamlama
/// yeniden denendiğinde aynı kullanıcıyı buluyor, hesap açılışı da wallet'ta tekrar
/// edilebilir.
/// </summary>
public sealed class RegistrationService(
    IDbContextFactory<OnboardingDbContext> contexts,
    IIdentityProvider identityProvider,
    IEmailSender emails,
    WalletAccountsClient wallet,
    TimeProvider time)
{
    public async Task<RegistrationStarted> StartAsync(string email, CancellationToken ct)
    {
        var code = VerificationCode.New();
        var registration = Registration.Start(Guid.NewGuid(), email, code, time.GetUtcNow());

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            db.Registrations.Add(registration);
            await db.SaveChangesAsync(ct);
        }

        // Kayıt yazıldıktan SONRA: kod gitmişken kayıt yazılamasaydı müşteri geçersiz
        // bir kod beklerdi. Gönderim düşerse müşteri kaydı yeniden başlatıyor.
        //
        // Adres kayıtlı olsa da kod gidiyor ve cevap aynı: burada "bu adres kayıtlı"
        // demek, başkasının e-postasının müşteri olup olmadığını verirdi. Adresin sahibi
        // olduğu kanıtlandıktan sonra, tamamlamada söyleniyor.
        await emails.SendAsync(
            registration.Email,
            "HiWallet doğrulama kodu",
            $"Kayıt kodun: {code}\n\nKod {Registration.CodeLifetime.TotalMinutes:0} dakika geçerli. " +
            "Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.",
            ct);

        return new RegistrationStarted(registration.Id, registration.CodeExpiresAt);
    }

    public async Task VerifyEmailAsync(Guid registrationId, string code, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var registration = await FindAsync(db, registrationId, ct);

        var result = registration.VerifyEmail(code, time.GetUtcNow());

        // Yanlış deneme de yazılıyor: sayılmasa deneme sınırı yalnızca kâğıt üstünde kalırdı.
        await db.SaveChangesAsync(ct);

        ThrowIfRejected(result);
    }

    public async Task<RegistrationCompleted> CompleteAsync(Guid registrationId, string password, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var registration = await FindAsync(db, registrationId, ct);
        var now = time.GetUtcNow();

        if (registration is { CompletedAt: not null, AccountId: { } completedAccount })
        {
            return new RegistrationCompleted(completedAccount, registration.Email, Replayed: true);
        }

        if (registration.EmailVerifiedAt is null)
        {
            throw new OnboardingRuleException(OnboardingRules.EmailNotVerified, "E-posta doğrulanmadı.");
        }

        if (!registration.CanComplete(now))
        {
            throw new OnboardingRuleException(
                OnboardingRules.RegistrationExpired, "Kaydın süresi doldu; kaydı yeniden başlat.");
        }

        if (registration.Subject is null)
        {
            registration.AttachIdentity(await CreateOrAdoptUserAsync(db, registration, password, ct));

            // Kullanıcı kayda hemen bağlanıyor: hesap açılışı düşerse tekrar deneme aynı
            // kullanıcıyla devam ediyor.
            await db.SaveChangesAsync(ct);
        }

        var accountId = await wallet.OpenPersonAccountAsync(registration.Subject!, ct);

        registration.Complete(accountId, now);
        await db.SaveChangesAsync(ct);

        return new RegistrationCompleted(accountId, registration.Email, Replayed: false);
    }

    /// <summary>
    /// Kimlik sağlayıcıda kullanıcı açar. Bu e-postayla kullanıcı zaten varsa:
    /// kendi kendine kayıt kapalı, kimlik sağlayıcıdaki her müşteriyi bu servis açtı ve
    /// ancak adresin sahibi kodu doğruladıktan sonra. Kullanıcı tamamlanmış bir kayda
    /// bağlıysa hesap var; değilse yarıda kalmış bir kaydın kullanıcısı, yine aynı
    /// adresin sahibinin. O kullanıcı yeni parolayla bu kayda bağlanıyor.
    /// </summary>
    private async Task<string> CreateOrAdoptUserAsync(
        OnboardingDbContext db, Registration registration, string password, CancellationToken ct)
    {
        try
        {
            return await identityProvider.CreateUserAsync(registration.Email, password, ct);
        }
        catch (IdentityConflictException)
        {
            // Aşağıda.
        }
        catch (PasswordRejectedException rejected)
        {
            throw new OnboardingRuleException(OnboardingRules.PasswordRejected, rejected.Message);
        }

        var existing = await identityProvider.FindByEmailAsync(registration.Email, ct)
                       ?? throw new InvalidOperationException(
                           "Kimlik sağlayıcı e-postanın kayıtlı olduğunu söyledi ama kullanıcı bulunamadı.");

        var registered = await db.Registrations
            .AnyAsync(r => r.Subject == existing.Subject && r.CompletedAt != null, ct);

        if (registered)
        {
            throw new OnboardingConflictException(
                OnboardingRules.EmailRegistered, "Bu e-postayla bir hesap var. Giriş yap ya da parolanı sıfırla.");
        }

        try
        {
            await identityProvider.SetPasswordAsync(existing.Subject, password, ct);
        }
        catch (PasswordRejectedException rejected)
        {
            throw new OnboardingRuleException(OnboardingRules.PasswordRejected, rejected.Message);
        }

        return existing.Subject;
    }

    private static async Task<Registration> FindAsync(OnboardingDbContext db, Guid registrationId, CancellationToken ct) =>
        await db.Registrations.SingleOrDefaultAsync(r => r.Id == registrationId, ct)
        ?? throw new OnboardingNotFoundException("Kayıt bulunamadı.");

    internal static void ThrowIfRejected(CodeCheck result)
    {
        switch (result)
        {
            case CodeCheck.WrongCode:
                throw new OnboardingRuleException(OnboardingRules.WrongCode, "Kod yanlış.");
            case CodeCheck.Expired:
                throw new OnboardingRuleException(OnboardingRules.CodeExpired, "Kodun süresi doldu; yeni kod iste.");
            case CodeCheck.Locked:
                throw new OnboardingRuleException(
                    OnboardingRules.TooManyAttempts, "Çok fazla yanlış deneme; yeni kod iste.");
        }
    }
}

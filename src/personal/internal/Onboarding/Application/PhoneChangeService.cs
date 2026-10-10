using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using HiWallet.Onboarding.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.Onboarding.Application;

public sealed class PhoneChangeOptions
{
    public const string SectionName = "PhoneChange";

    /// <summary>
    /// Parolayla son girişten bu yana geçebilecek en uzun süre. Açık kalmış bir oturum
    /// numarayı değiştirmeye yetmiyor; kodun gelmesi için birkaç dakika pay var.
    /// </summary>
    public TimeSpan ReauthenticationWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Değişiklikten sonra bankaya çekimin kapalı kaldığı süre; transfer ve ödeme açık.</summary>
    public TimeSpan WithdrawalHold { get; set; } = TimeSpan.FromHours(24);
}

/// <param name="WithdrawalHoldUntil">
/// Bankaya çekimin açılacağı an. Aynı değişikliğin tekrar edilen onayında <c>null</c>: değişiklik
/// önceki istekte yapıldı.
/// </param>
public sealed record PhoneChanged(PhoneNumber Phone, DateTimeOffset? WithdrawalHoldUntil);

/// <summary>
/// Temel doğrulamadan sonra telefon değiştirme. Numara hesabı ele geçirmenin ilk adımı:
/// parolayla yeni bir giriş isteniyor, kod yeni numaraya gidiyor, eski numaraya ve
/// e-postaya haber veriliyor, eski numara değişiklik kaydında kalıyor ve bankaya çekim bir
/// süre kapanıyor. Eski numaradan kod istenmiyor: değiştirmenin en sık sebebi eski hattın
/// kaybolması.
/// </summary>
public sealed class PhoneChangeService(
    IDbContextFactory<OnboardingDbContext> contexts,
    ISmsSender sms,
    IEmailSender emails,
    WalletAccountsClient wallet,
    IOptions<PhoneChangeOptions> options,
    TimeProvider time,
    ILogger<PhoneChangeService> logger)
{
    /// <param name="authTime">Token'daki son parolalı giriş (<c>auth_time</c>).</param>
    public async Task<PhoneVerificationStarted> StartAsync(
        string subject, DateTimeOffset? authTime, PhoneNumber phone, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        EnsureFresh(authTime, now);

        var code = VerificationCode.New();
        var verification = PhoneVerification.Start(
            Guid.NewGuid(), subject, phone, code, now, PhoneVerificationPurpose.Change);

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Subject == subject, ct);

            if (customer?.BasicVerifiedAt is null)
            {
                throw new OnboardingRuleException(
                    OnboardingRules.VerificationIncomplete, "Numaran temel doğrulamanın telefon adımında değişiyor.");
            }

            if (customer.Phone == phone)
            {
                throw new OnboardingRuleException(OnboardingRules.SamePhone, "Bu numara zaten senin numaran.");
            }

            db.PhoneVerifications.Add(verification);
            await db.SaveChangesAsync(ct);
        }

        await sms.SendAsync(
            phone,
            $"HiWallet numara değişikliği kodun: {code}. Kod {Registration.CodeLifetime.TotalMinutes:0} dakika geçerli.",
            ct);

        return new PhoneVerificationStarted(verification.Id, phone, verification.ExpiresAt);
    }

    public async Task<PhoneChanged> ConfirmAsync(
        string subject, DateTimeOffset? authTime, Guid verificationId, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        EnsureFresh(authTime, now);

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Başkasının doğrulaması da ilk doğrulamanınki de yokmuş gibi: 404.
        var verification = await db.PhoneVerifications
                               .SingleOrDefaultAsync(
                                   v => v.Id == verificationId
                                        && v.Subject == subject
                                        && v.Purpose == PhoneVerificationPurpose.Change,
                                   ct)
                           ?? throw new OnboardingNotFoundException("Numara değişikliği bulunamadı.");

        var result = verification.Confirm(code, now);

        if (result is CodeCheck.AlreadyVerified)
        {
            // Önceki istek değişikliği yaptı, cevabı kayboldu: aynı sonuç.
            return new PhoneChanged(verification.Phone, WithdrawalHoldUntil: null);
        }

        if (result is not CodeCheck.Verified)
        {
            // Yanlış deneme sayılıyor; beşinci yanlışta kod kilitleniyor.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            RegistrationService.ThrowIfRejected(result);
        }

        var customer = await db.Customers
            .FromSql($"SELECT * FROM customers WHERE subject = {subject} FOR UPDATE")
            .SingleAsync(ct);

        await PhoneOwnership.EnsureFreeAsync(db, subject, verification.Phone, ct);

        var registration = await db.Registrations
                               .Where(r => r.Subject == subject && r.CompletedAt != null)
                               .Select(r => new { r.AccountId, r.Email })
                               .FirstOrDefaultAsync(ct)
                           ?? throw new OnboardingRuleException(
                               OnboardingRules.RegistrationMissing, "Bu kimliğin tamamlanmış bir kaydı yok.");

        // Önce wallet, sonra numara: wallet çağrısı düşerse numara değişmiyor ve tekrar deneme
        // baştan geçiyor. Yarım kalan iş kısıtlayıcı tarafta kalıyor; bekletme numara
        // değişmeden de zararsız.
        var holdUntil = await wallet.HoldWithdrawalsAsync(
            registration.AccountId!.Value, now + options.Value.WithdrawalHold, ct);

        var oldPhone = customer.ChangePhone(verification.Phone, now);
        db.PhoneChanges.Add(PhoneChange.Of(subject, oldPhone, verification.Phone, now));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await NotifyAsync(oldPhone, registration.Email, verification.Phone, holdUntil, ct);

        return new PhoneChanged(verification.Phone, holdUntil);
    }

    private void EnsureFresh(DateTimeOffset? authTime, DateTimeOffset now)
    {
        if (authTime is null || now - authTime.Value > options.Value.ReauthenticationWindow)
        {
            throw new ReauthenticationRequiredException("Numaranı değiştirmek için parolanla yeniden giriş yap.");
        }
    }

    /// <summary>
    /// Değişikliği müşteri yapmadıysa görsün: eski numaraya ve e-postaya. Bildirim gitmezse
    /// değişiklik geri alınmıyor; log'a düşüyor.
    /// </summary>
    private async Task NotifyAsync(
        PhoneNumber? oldPhone, string email, PhoneNumber newPhone, DateTimeOffset holdUntil, CancellationToken ct)
    {
        const string NotYou = "Bu değişikliği sen yapmadıysan hemen bize ulaş.";

        if (oldPhone is { } old)
        {
            try
            {
                await sms.SendAsync(old, $"HiWallet hesabındaki telefon numarası değişti. {NotYou}", ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Numara değişikliği eski numaraya bildirilemedi.");
            }
        }

        try
        {
            await emails.SendAsync(
                email,
                "Telefon numaran değişti",
                $"HiWallet hesabındaki telefon numarası {newPhone.Masked} olarak değişti. Güvenliğin için " +
                $"{holdUntil:dd.MM.yyyy HH:mm} (UTC) anına kadar banka hesabına para çekilemiyor. {NotYou}",
                ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Numara değişikliği e-postayla bildirilemedi.");
        }
    }
}

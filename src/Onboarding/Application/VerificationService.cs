using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using HiWallet.Onboarding.Infrastructure.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HiWallet.Onboarding.Application;

public sealed record PhoneVerificationStarted(Guid VerificationId, PhoneNumber Phone, DateTimeOffset ExpiresAt);

public sealed record BasicVerificationCompleted(Guid AccountId, string KycLevel);

/// <param name="Email">Kaydın e-postası; kayıt onboarding'den geçmediyse <c>null</c>.</param>
public sealed record OnboardingStatus(
    string? Email,
    PhoneNumber? Phone,
    bool PhoneVerified,
    bool IdentityVerified,
    bool BasicVerificationCompleted,
    DocumentOptions Documents);

/// <summary>
/// Temel doğrulama, müşteri giriş yaptıktan sonra: telefon, kimlik bilgileri ve onaylar.
/// Kimlik token'daki <c>sub</c>; müşteri yalnızca kendi kaydını görüyor.
/// </summary>
public sealed class VerificationService(
    IDbContextFactory<OnboardingDbContext> contexts,
    ISmsSender sms,
    IPopulationRegistry populationRegistry,
    WalletAccountsClient wallet,
    IOptions<DocumentOptions> documents,
    TimeProvider time)
{
    /// <summary>Temel doğrulama tamamlanınca hesabın ulaştığı seviye.</summary>
    private const string BasicLevel = "Unverified";

    public async Task<PhoneVerificationStarted> StartPhoneAsync(string subject, PhoneNumber phone, CancellationToken ct)
    {
        var code = VerificationCode.New();
        var verification = PhoneVerification.Start(Guid.NewGuid(), subject, phone, code, time.GetUtcNow());

        await using (var db = await contexts.CreateDbContextAsync(ct))
        {
            db.PhoneVerifications.Add(verification);
            await db.SaveChangesAsync(ct);
        }

        await sms.SendAsync(
            phone,
            $"HiWallet doğrulama kodun: {code}. Kod {Registration.CodeLifetime.TotalMinutes:0} dakika geçerli.",
            ct);

        return new PhoneVerificationStarted(verification.Id, phone, verification.ExpiresAt);
    }

    public async Task<PhoneNumber> ConfirmPhoneAsync(string subject, Guid verificationId, string code, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Başkasının doğrulaması yokmuş gibi: 404, 403 değil.
        var verification = await db.PhoneVerifications
                               .SingleOrDefaultAsync(v => v.Id == verificationId && v.Subject == subject, ct)
                           ?? throw new OnboardingNotFoundException("Telefon doğrulaması bulunamadı.");

        var now = time.GetUtcNow();
        var result = verification.Confirm(code, now);

        if (result is CodeCheck.Verified)
        {
            var customer = await LockCustomerAsync(db, subject, now, ct);
            customer.PhoneVerified(verification.Phone, now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        RegistrationService.ThrowIfRejected(result);

        return verification.Phone;
    }

    public async Task<NationalId> VerifyIdentityAsync(
        string subject, string firstName, string lastName, NationalId nationalId, DateOnly birthDate, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = time.GetUtcNow();
        var customer = await LockCustomerAsync(db, subject, now, ct);

        if (customer.BasicVerifiedAt is not null)
        {
            throw new OnboardingRuleException(
                OnboardingRules.IdentityLocked, "Temel doğrulaması tamamlanmış müşterinin kimlik bilgileri değişmez.");
        }

        var usedByOther = await db.Customers
            .AnyAsync(c => c.NationalId == nationalId && c.Subject != subject, ct);

        if (usedByOther)
        {
            throw NationalIdRegistered();
        }

        if (!await populationRegistry.MatchesAsync(nationalId, firstName, lastName, birthDate.Year, ct))
        {
            throw new OnboardingRuleException(
                OnboardingRules.IdentityMismatch, "Kimlik bilgileri nüfus kaydıyla eşleşmedi.");
        }

        customer.IdentityVerified(firstName, lastName, nationalId, birthDate, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı numara başka bir müşteride aynı anda doğrulandı; yukarıdaki kontrol
            // ile yazma arasında.
            throw NationalIdRegistered();
        }

        await transaction.CommitAsync(ct);

        return nationalId;
    }

    public async Task<BasicVerificationCompleted> CompleteBasicAsync(
        string subject, string termsVersion, string privacyNoticeVersion, CancellationToken ct)
    {
        var current = documents.Value;

        if (termsVersion != current.Terms || privacyNoticeVersion != current.PrivacyNotice)
        {
            throw new OnboardingRuleException(
                OnboardingRules.DocumentOutdated, "Onaylanan metin güncel değil; güncel sürümü göster ve yeniden onaylat.");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Subject == subject, ct);

        if (customer is not { ReadyForBasicVerification: true })
        {
            throw new OnboardingRuleException(
                OnboardingRules.VerificationIncomplete, "Önce telefonu ve kimlik bilgilerini doğrula.");
        }

        var accountId = await db.Registrations
                            .Where(r => r.Subject == subject && r.CompletedAt != null)
                            .Select(r => r.AccountId)
                            .FirstOrDefaultAsync(ct)
                        ?? throw new OnboardingRuleException(
                            OnboardingRules.RegistrationMissing, "Bu kimliğin tamamlanmış bir kaydı yok.");

        var now = time.GetUtcNow();

        // Onaylar tekrar eden istekte ikinci kez yazılmıyor. Satırlar değişmiyor ve
        // silinmiyor (REVOKE); onayın kanıtı.
        foreach (var consent in new[]
                 {
                     Consent.Accept(subject, ConsentDocument.Terms, termsVersion, now),
                     Consent.Accept(subject, ConsentDocument.PrivacyNotice, privacyNoticeVersion, now)
                 })
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO consents (id, subject, document, version, accepted_at)
                 VALUES ({consent.Id}, {consent.Subject}, {ConsentText(consent.Document)}, {consent.Version}, {consent.AcceptedAt})
                 ON CONFLICT (subject, document, version) DO NOTHING
                 """,
                ct);
        }

        // Önce wallet, sonra kayıt: wallet çağrısı düşerse hiçbir şey işaretlenmiyor ve
        // tekrar deneme baştan geçiyor. Wallet'ta yükseltme tekrar edilebilir.
        var level = await wallet.RaiseKycLevelAsync(accountId, BasicLevel, ct);

        customer.BasicVerified(now);
        await db.SaveChangesAsync(ct);

        return new BasicVerificationCompleted(accountId, level);
    }

    public async Task<OnboardingStatus> StatusAsync(string subject, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var email = await db.Registrations
            .Where(r => r.Subject == subject)
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => r.Email)
            .FirstOrDefaultAsync(ct);

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Subject == subject, ct);

        return new OnboardingStatus(
            email,
            customer?.Phone,
            customer?.PhoneVerifiedAt is not null,
            customer?.IdentityVerifiedAt is not null,
            customer?.BasicVerifiedAt is not null,
            documents.Value);
    }

    /// <summary>
    /// Müşteri satırını açar (yoksa) ve kilitler. Aynı müşterinin eşzamanlı iki isteği
    /// satırı sırayla güncelliyor; ilk isteklerin ikisi de satırı açmaya çalışırsa biri
    /// ON CONFLICT'e takılıyor.
    /// </summary>
    private static async Task<Customer> LockCustomerAsync(
        OnboardingDbContext db, string subject, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO customers (subject, created_at, updated_at)
             VALUES ({subject}, {now}, {now})
             ON CONFLICT (subject) DO NOTHING
             """,
            ct);

        return await db.Customers
            .FromSql($"SELECT * FROM customers WHERE subject = {subject} FOR UPDATE")
            .SingleAsync(ct);
    }

    private static OnboardingConflictException NationalIdRegistered() =>
        new(OnboardingRules.NationalIdRegistered, "Bu kimlik numarası başka bir hesapta kayıtlı.");

    // ValueConverters'taki eşlemenin ve ck_consents_document'ın aynısı.
    private static string ConsentText(ConsentDocument document) => document switch
    {
        ConsentDocument.Terms => "terms",
        ConsentDocument.PrivacyNotice => "privacy_notice",
        _ => throw new ArgumentOutOfRangeException(nameof(document))
    };
}

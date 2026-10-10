using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Onboarding.Application;

/// <summary>
/// Bir numara tek müşteride. Kontrol veritabanının tekil index'iyle değil uygulamada: kural
/// sonradan geldi ve kurulu ortamlarda aynı numarayla doğrulanmış iki müşteri olabilir;
/// index migration'ı onları bulunca açılmazdı. Kontrol ilk doğrulamada da değişiklikte de
/// koşuyor ve kod doğrulandıktan SONRA: başkasının numarası olduğu, numaranın sahibi
/// olduğunu kanıtlamayana söylenmiyor.
/// </summary>
internal static class PhoneOwnership
{
    /// <summary>
    /// Numara başka bir müşteride doğrulanmışsa <c>409</c>. Transaction içinde çağrılmalı: aynı
    /// numarayı aynı anda doğrulayan iki müşteri numara başına kilitle sıraya giriyor.
    /// </summary>
    public static async Task EnsureFreeAsync(OnboardingDbContext db, string subject, PhoneNumber phone, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({phone.Value}))", ct);

        var taken = await db.Customers.AnyAsync(
            c => c.Subject != subject && c.Phone == phone && c.PhoneVerifiedAt != null, ct);

        if (taken)
        {
            throw new OnboardingConflictException(OnboardingRules.PhoneInUse, "Bu numara başka bir müşteride kayıtlı.");
        }
    }
}

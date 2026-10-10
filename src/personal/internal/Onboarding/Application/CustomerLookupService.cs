using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Onboarding.Application;

/// <param name="Customer">Doğrulamaya başlamamış müşteride <c>null</c>: yalnızca kaydı var.</param>
/// <param name="PhoneChanges">Yeniden eskiye.</param>
public sealed record CustomerProfile(
    Guid AccountId,
    string Email,
    Customer? Customer,
    IReadOnlyList<Consent> Consents,
    IReadOnlyList<PhoneChange> PhoneChanges);

/// <param name="Customer">Doğrulamaya başlamamış müşteride <c>null</c>.</param>
public sealed record CustomerMatch(Guid AccountId, string Email, Customer? Customer);

/// <summary>
/// Çalışanın müşteriyi tanıması: hesabın sahibi kim ve bir e-posta, telefon ya da kimlik
/// numarası hangi hesaba ait. Yalnızca okuyor. Kayıt üzerinden bağlanıyor: hesap kaydın
/// sonunda açıldı, kaydın <c>sub</c>'ı müşterinin anahtarı.
/// </summary>
public sealed class CustomerLookupService(IDbContextFactory<OnboardingDbContext> contexts)
{
    /// <summary>Aramanın üst sınırı: arama bir müşteriyi bulmak için, liste dökmek için değil.</summary>
    public const int MaxMatches = 20;

    /// <summary>Onboarding'den açılmamış hesap (işyeri) ya da olmayan hesap <c>404</c>.</summary>
    public async Task<CustomerProfile> ByAccountAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var registration = await db.Registrations.AsNoTracking()
            .Where(r => r.AccountId == accountId)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new OnboardingNotFoundException("Hesabın kaydı yok.");

        var subject = registration.Subject!;

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Subject == subject, ct);

        // İki liste de müşteri başına birkaç satır: onay metin sürümü başına, numara
        // değişikliği müşterinin kendi isteğiyle.
        var consents = await db.Consents.AsNoTracking()
            .Where(c => c.Subject == subject)
            .OrderBy(c => c.AcceptedAt)
            .ToListAsync(ct);

        var phoneChanges = await db.PhoneChanges.AsNoTracking()
            .Where(c => c.Subject == subject)
            .OrderByDescending(c => c.ChangedAt)
            .ToListAsync(ct);

        return new CustomerProfile(accountId, registration.Email, customer, consents, phoneChanges);
    }

    /// <summary>E-posta kayıttaki gibi küçük harfe indirilip karşılaştırılıyor.</summary>
    public Task<IReadOnlyList<CustomerMatch>> ByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = Registration.NormalizeEmail(email);

        return SearchAsync(db => db.Registrations.Where(r => r.Email == normalized).Select(r => r.Subject!), ct);
    }

    public Task<IReadOnlyList<CustomerMatch>> ByPhoneAsync(PhoneNumber phone, CancellationToken ct) =>
        SearchAsync(db => db.Customers.Where(c => c.Phone == phone).Select(c => c.Subject), ct);

    public Task<IReadOnlyList<CustomerMatch>> ByNationalIdAsync(NationalId nationalId, CancellationToken ct) =>
        SearchAsync(db => db.Customers.Where(c => c.NationalId == nationalId).Select(c => c.Subject), ct);

    /// <summary>Hesabı açılmış kayıtlar; kaydı yarım kalanın hesabı yok, aranacak bir şey değil.</summary>
    private async Task<IReadOnlyList<CustomerMatch>> SearchAsync(
        Func<OnboardingDbContext, IQueryable<string>> subjects, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var matching = subjects(db);

        var registrations = await db.Registrations.AsNoTracking()
            .Where(r => r.AccountId != null && matching.Contains(r.Subject!))
            .OrderByDescending(r => r.CompletedAt)
            .Take(MaxMatches)
            .ToListAsync(ct);

        var found = registrations.Select(r => r.Subject!).ToList();

        var customers = await db.Customers.AsNoTracking()
            .Where(c => found.Contains(c.Subject))
            .ToDictionaryAsync(c => c.Subject, ct);

        return
        [
            .. registrations.Select(r =>
                new CustomerMatch(r.AccountId!.Value, r.Email, customers.GetValueOrDefault(r.Subject!)))
        ];
    }
}

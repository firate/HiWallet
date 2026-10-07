using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Onboarding.Application;

/// <summary>
/// Bir kimlik numarasının bir hesap sahibine ait olup olmadığı. Soran wallet: havalenin
/// müşterinin kendi adına kayıtlı hesabından geldiğini doğruluyor. Numara wallet'ta
/// tutulmuyor ve geri verilmiyor; cevap yalnızca evet ya da hayır.
/// </summary>
public sealed class HolderCheckService(IDbContextFactory<OnboardingDbContext> contexts)
{
    /// <returns>
    /// Sahibin nüfus kaydıyla doğrulanmış kimlik numarası bu mu. Sahip yoksa, kimliği
    /// doğrulanmamışsa ya da numara kurala uymuyorsa <c>false</c>: sebebi soran tarafa
    /// söylenmiyor.
    /// </returns>
    public async Task<bool> IsHolderAsync(string subject, string nationalId, CancellationToken ct)
    {
        if (!NationalId.TryParse(nationalId, out var id))
        {
            return false;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);

        return await db.Customers.AnyAsync(
            c => c.Subject == subject && c.IdentityVerifiedAt != null && c.NationalId == id, ct);
    }
}

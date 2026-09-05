using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// Saga'nın okunması. Ayrı sınıf çünkü yazma yolundan farklı: burada değişiklik
/// izlemeye gerek yok ve saga hiç yüklenmeden de cevap verilebiliyor.
/// </summary>
public sealed class WithdrawalQueries(IDbContextFactory<OrchestratorDbContext> contextFactory)
{
    public async Task<WithdrawalSaga?> FindAsync(Guid withdrawalId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.Sagas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == withdrawalId, ct);
    }
}

using HiWallet.Bank.Fake.Api.Responses;
using HiWallet.Bank.Fake.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Transferin okunması. Ayrı sınıf çünkü yazma yolundan farklı: değişiklik
/// izlemeye gerek yok ve tek bir satır okunuyor. Orchestrator'daki
/// <c>WithdrawalQueries</c> ile aynı kalıp — controller veritabanına doğrudan
/// dokunmuyor.
///
/// <b>Yanıt tipini döndürüyor, entity'yi değil</b> ve bu <c>WithdrawalQueries</c>'ten
/// sapma. Sebebi burada durumun TÜRETİLMİŞ olması: <see cref="TransferResolution"/>
/// zamana bakarak karar veriyor ve o karar tek yerde kalmalı. Entity dönseydi
/// türetmeyi controller yapardı; callback göndericisi de kendi tarafında yapıyor
/// ve ikisi ayrıştığında sahte banka aynı transfer için sorguda başka, callback'te
/// başka şey söylerdi.
/// </summary>
public sealed class TransferQueries(
    IDbContextFactory<BankFakeDbContext> contextFactory, TimeProvider timeProvider)
{
    public async Task<TransferStatusResponse?> FindAsync(string bankReference, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var transfer = await db.Transfers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.BankReference == bankReference, ct);

        if (transfer is null) return null;

        var status = TransferResolution.StatusOf(transfer, timeProvider.GetUtcNow());

        return new TransferStatusResponse(
            transfer.BankReference,
            transfer.ClientReference,
            status,
            transfer.Amount,
            transfer.Fee,
            transfer.Currency,
            status is TransferStatus.Failed ? TransferResolution.FailureReason : null,
            transfer.AcceptedAt);
    }
}

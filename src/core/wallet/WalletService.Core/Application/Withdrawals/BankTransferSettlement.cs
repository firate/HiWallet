using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <param name="Owed">Clearing'de kapanan tutar: transferle giden para.</param>
/// <param name="LeavingBank">Nostro'dan çıkan: Net modelde ücret dahil.</param>
internal sealed record BankTransferSettlementResult(
    LedgerTransaction Transaction, decimal Owed, decimal LeavingBank, FeeSettlement Model);

/// <summary>
/// Bankadan giden transferin muhasebesini kapatır (<c>ledger-schema.md</c> "Withdrawal
/// settlement"). Çekim ve askıdaki havalenin iadesi aynı kaydı yazıyor; iki kopya olsaydı
/// ücret modeli birinde değişip öbüründe eskirdi.
///
/// <code>
/// clearing         -100     transferi başlatan kayıtta açılan borç kapanır
/// provider_expense  -1.5    yalnızca Net modelde
/// nostro          +101.5    banka hesabından çıkan gerçek tutar
/// </code>
///
/// <b>Tutar orijinal işlemden okunuyor</b>, komuttan gelmiyor: clearing'e ne yazıldığını
/// wallet biliyor. Ücreti yalnızca banka biliyor; o komuttan geliyor. Invoiced modelde gider
/// bacağı yok: ücret dönem sonu faturasıyla ödeniyor (madde 10).
/// </summary>
internal static class BankTransferSettlement
{
    /// <param name="original">Clearing'e borç yazan kayıt: çekimin düşmesi ya da askıdan düşme.</param>
    public static async Task<BankTransferSettlementResult> RecordAsync(
        WalletDbContext db,
        ProviderPolicy providers,
        string provider,
        LedgerTransaction original,
        decimal fee,
        string bankReference,
        Guid transactionId,
        Guid sagaId,
        string idempotencyKey,
        Actor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var currency = original.Entries[0].Money.Currency;
        var clearing = await LoadSystemAccountAsync(db, provider, LedgerAccountType.Clearing, currency, ct);

        // Transferi başlatan kayıtta clearing'e YAZILAN tutar. Pozitifti (ödenecek para,
        // yolda); settlement onu kapatıyor.
        var owed = original.Entries
            .Where(e => e.LedgerAccountId == clearing.Id)
            .Sum(e => e.Money.Amount);

        if (owed <= 0m)
        {
            throw new InvalidOperationException(
                $"Saga {sagaId} kaydında clearing bacağı beklenen yönde değil: {owed}.");
        }

        var terms = providers.For(provider);

        var tx = LedgerTransaction.Create(
            transactionId, LedgerTransactionType.Settlement, clearing.Id, actor, now, idempotencyKey, sagaId);

        // Transfer cash kovasından çıktı (decisions.md madde 36); onu kapatan settlement de
        // aynı kovada, yoksa clearing'in cash kovası kalıcı olarak açık kalırdı.
        tx.AddEntry(clearing.Id, new Money(-owed, currency), FundType.Cash);

        var leavingBank = owed;

        if (terms.FeeSettlement is FeeSettlement.Net && fee > 0m)
        {
            var expense = await LoadSystemAccountAsync(db, provider, LedgerAccountType.ProviderExpense, currency, ct);

            tx.AddEntry(expense.Id, new Money(-fee, currency), FundType.Cash);
            leavingBank += fee;
        }

        var nostro = await LoadSystemAccountAsync(db, provider, LedgerAccountType.Nostro, currency, ct);

        // nostro POZİTİF: varlık hesabı ve bankadan para çıkıyor, sıfıra doğru.
        tx.AddEntry(nostro.Id, new Money(leavingBank, currency), FundType.Cash);

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        // Ücret tahakkuku (ledger DEĞİL). Invoiced modelde satır faturayla kapanacak; Net
        // modelde gider yukarıda yazıldı ve satır gerçekleşen tutarla açılıyor.
        if (fee > 0m)
        {
            db.ProviderFees.Add(new ProviderFee
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                Provider = provider,
                SettlementModel = terms.FeeSettlement,
                ExpectedAmount = fee,
                ActualAmount = terms.FeeSettlement is FeeSettlement.Net ? fee : null,
                Currency = currency.Code,
                ProviderRef = bankReference,
                LedgerTransactionId = terms.FeeSettlement is FeeSettlement.Net ? transactionId : null,
                OccurredAt = now
            });
        }

        // Sıra ledger hesap kimliğine göre ARTAN (decisions.md madde 8). Hepsi sistem
        // hesabı (madde 6); bacak hangi kovaya yazıldıysa bakiye de o kovada.
        foreach (var entry in tx.Entries.OrderBy(e => e.LedgerAccountId))
        {
            var balance = await db.LedgerBalances
                              .FirstOrDefaultAsync(
                                  b => b.LedgerAccountId == entry.LedgerAccountId && b.FundType == entry.FundType, ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {entry.LedgerAccountId}");

            balance.Apply(entry.Money, canGoNegative: true, now);
        }

        return new BankTransferSettlementResult(tx, owed, leavingBank, terms.FeeSettlement);
    }

    private static async Task<LedgerAccount> LoadSystemAccountAsync(
        WalletDbContext db, string provider, LedgerAccountType type, Currency currency, CancellationToken ct) =>
        await db.LedgerAccounts.FirstOrDefaultAsync(
            a => a.Type == type && a.Provider == provider && a.Currency == currency, ct)
        ?? throw new InvalidOperationException($"'{provider}' sağlayıcısının {currency} {type} hesabı yok.");
}

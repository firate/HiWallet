namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Mutabakat bulguları (<c>overview.md</c> madde 7, <c>decisions.md</c> madde 11).
///
/// <b>Sistem düzeltmiyor, gösteriyor.</b> Takılmış saga taraması ve fatura
/// uyuşmazlığıyla aynı ilke: bulgunun hangi tarafın hatasından geldiğine sistem
/// karar veremez, kararı insan veriyor.
/// </summary>
internal sealed record ReconciliationReport(
    IReadOnlyList<BalanceDrift> Drifts,
    IReadOnlyList<AgingClearingItem> AgingItems,
    IReadOnlyList<PendingInvoice> PendingInvoices,
    IReadOnlyList<OverdueFees> OverdueFees)
{
    public bool IsClean =>
        Drifts.Count == 0
        && AgingItems.Count == 0
        && PendingInvoices.Count == 0
        && OverdueFees.Count == 0;
}

/// <summary>
/// Projeksiyon ile ledger ayrışmış: <c>ledger_balances.balance</c>, o hesabın
/// <c>ledger_entries</c> toplamına eşit değil.
///
/// <b>Bunu başka hiçbir şey yakalamıyor.</b> Zero-sum invariant'ı DB trigger'ıyla
/// zorlanıyor ama o, bir işlemin bacaklarının toplamına bakıyor — projeksiyonun
/// doğru güncellendiğine değil. <c>ledger-schema.md</c> "bu tablo her zaman yeniden
/// inşa edilebilir" diyor; o iddiayı sınayan tek yer burası.
/// </summary>
internal sealed record BalanceDrift(
    Guid LedgerAccountId, decimal Projected, decimal FromEntries)
{
    public decimal Difference => Projected - FromEntries;
}

/// <summary>
/// Uzun süredir <c>clearing</c>'de bekleyen para: sağlayıcı settlement göndermemiş.
/// Clearing "yolda olan para" demek; yolda kalması normal, UZUN SÜRE kalması değil.
/// </summary>
internal sealed record AgingClearingItem(
    string Provider, int Count, decimal Amount, DateTimeOffset Oldest);

/// <summary>Uyuşmazlık nedeniyle incelemede bekleyen fatura (madde 11).</summary>
internal sealed record PendingInvoice(
    string Provider, string InvoiceRef, decimal Amount, decimal Expected, DateTimeOffset ReceivedAt);

/// <summary>
/// Faturası gecikmiş ücretler: <c>Invoiced</c> modeldeki sağlayıcı dönemi kapattı
/// ama fatura gelmedi. Gider defterde tahakkuk etmiş görünüyor, ödenmemiş.
/// </summary>
internal sealed record OverdueFees(
    string Provider, int Count, decimal ExpectedTotal, DateTimeOffset Oldest);

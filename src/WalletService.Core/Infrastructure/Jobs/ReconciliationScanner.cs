using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.WalletService.Infrastructure.Jobs;

/// <summary>
/// Mutabakatın kendisi. Zamanlamadan ayrı duruyor ki test saatlerce beklemesin ve
/// log metnine değil bulgulara baksın.
///
/// Dört soru soruyor:
/// <list type="number">
/// <item>Projeksiyon ledger ile tutuyor mu?</item>
/// <item>Clearing'de yaşlanan para var mı — sağlayıcı ödemedi mi?</item>
/// <item>İncelemede bekleyen fatura var mı?</item>
/// <item>Faturası gecikmiş ücret var mı?</item>
/// </list>
///
/// İlk soru <c>overview.md</c>'de yok ve bilerek eklendi: zero-sum invariant'ı DB
/// trigger'ıyla zorlanıyor ama o, bir işlemin bacaklarının toplamına bakıyor —
/// <c>ledger_balances</c>'ın doğru güncellendiğine değil. Projeksiyonu uygulama
/// kodu yazıyor ve oradaki bir hata SESSİZCE ayrışma üretir. "Bu tablo her zaman
/// yeniden inşa edilebilir" iddiasını sınayan başka hiçbir şey yok.
/// </summary>
internal sealed class ReconciliationScanner(
    IDbContextFactory<WalletDbContext> contextFactory,
    ProviderPolicy providers,
    TimeProvider timeProvider,
    IOptions<ReconciliationOptions> options)
{
    private readonly ReconciliationOptions _options = options.Value;

    public async Task<ReconciliationReport> ScanAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return new ReconciliationReport(
            await FindDriftsAsync(db, ct),
            await FindAgingClearingAsync(db, now, ct),
            await FindPendingInvoicesAsync(db, ct),
            await FindOverdueFeesAsync(db, now, ct));
    }

    /// <summary>
    /// Projeksiyon–ledger karşılaştırması. Tek sorguda: bakiye satırları ile
    /// entry toplamları KOVA bazında yan yana getiriliyor.
    ///
    /// Gruplama (hesap, kaynak tipi) üzerinde, çünkü bakiyenin anahtarı da o
    /// (decisions.md madde 36). Yalnızca hesaba göre gruplansaydı tek kovanın
    /// bakiyesi hesabın TÜM hareketleriyle karşılaştırılır ve birden fazla kovası
    /// olan her hesap ayrışmış görünürdü.
    /// </summary>
    private async Task<IReadOnlyList<BalanceDrift>> FindDriftsAsync(
        WalletDbContext db, CancellationToken ct)
    {
        // Korelasyonlu alt sorgu: her bakiye satırı kendi kovasının entry toplamıyla
        // karşılaştırılıyor. GroupBy + GroupJoin bileşik anahtarla güvenilir
        // çevrilmiyordu; bu biçim tek sorguda kalıyor ve eşleşmeyi açıkça yazıyor.
        //
        // Hareketi hiç olmayan kova da kontrol ediliyor: bakiyesi sıfırdan farklıysa
        // o da ayrışmadır, bu yüzden toplam yokken sıfır sayılıyor.
        var drifts = await db.LedgerBalances
            .Select(balance => new
            {
                balance.LedgerAccountId,
                balance.FundType,
                balance.Balance,
                FromEntries = db.LedgerEntries
                    .Where(e => e.LedgerAccountId == balance.LedgerAccountId
                                && e.FundType == balance.FundType)
                    .Sum(e => (decimal?)e.Amount) ?? 0m
            })
            .Where(row => row.Balance != row.FromEntries)
            .Take(_options.SampleSize)
            .ToListAsync(ct);

        return drifts
            .Select(row => new BalanceDrift(
                row.LedgerAccountId, row.FundType, row.Balance, row.FromEntries))
            .ToArray();
    }

    /// <summary>
    /// Settlement'ı gelmemiş top-up'lar. Ölçüt <c>provider_fees.ledger_tx_id</c>:
    /// settlement o alanı dolduruyor, boşsa alacak hâlâ açık.
    ///
    /// Clearing BAKİYESİNE bakmak yerine satır bazına bakılıyor çünkü bakiye
    /// yalnızca "ne kadar açık" diyor; hangi işlemlerin açık kaldığını ve ne kadar
    /// beklediğini söylemiyor — madde 11'in fark analizi tam olarak bunu istiyor.
    /// </summary>
    private async Task<IReadOnlyList<AgingClearingItem>> FindAgingClearingAsync(
        WalletDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - _options.SettlementDue;

        var aging = await db.ProviderFees
            .Where(f => f.LedgerTransactionId == null && f.OccurredAt < cutoff)
            .GroupBy(f => f.Provider)
            .Select(g => new
            {
                Provider = g.Key,
                Count = g.Count(),
                Amount = g.Sum(f => f.ExpectedAmount),
                Oldest = g.Min(f => f.OccurredAt)
            })
            .OrderBy(row => row.Oldest)
            .Take(_options.SampleSize)
            .ToListAsync(ct);

        return aging
            .Select(row => new AgingClearingItem(row.Provider, row.Count, row.Amount, row.Oldest))
            .ToArray();
    }

    private async Task<IReadOnlyList<PendingInvoice>> FindPendingInvoicesAsync(
        WalletDbContext db, CancellationToken ct)
    {
        var pending = await db.ProviderInvoices
            .Where(i => i.Status == ProviderInvoiceStatus.PendingReview)
            .OrderBy(i => i.ReceivedAt)
            .Take(_options.SampleSize)
            .ToListAsync(ct);

        return pending
            .Select(i => new PendingInvoice(
                i.Provider, i.InvoiceRef, i.Amount, i.ExpectedAmount, i.ReceivedAt))
            .ToArray();
    }

    /// <summary>
    /// Faturası gecikmiş ücretler. Yalnızca <c>Invoiced</c> modeldeki sağlayıcılar:
    /// <c>Net</c> modelde fatura diye bir adım yok, oradaki satırların
    /// <c>invoice_ref</c>'i zaten hiç dolmuyor (madde 10) ve hepsini "gecikmiş"
    /// saymak raporu kullanılamaz hale getirirdi.
    /// </summary>
    private async Task<IReadOnlyList<OverdueFees>> FindOverdueFeesAsync(
        WalletDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - _options.InvoiceDue;

        var invoiced = providers.Providers
            .Where(name => providers.For(name).FeeSettlement is FeeSettlement.Invoiced)
            .ToArray();

        if (invoiced.Length == 0)
        {
            return [];
        }

        var overdue = await db.ProviderFees
            .Where(f => invoiced.Contains(f.Provider)
                        && f.InvoiceRef == null
                        && f.OccurredAt < cutoff)
            .GroupBy(f => f.Provider)
            .Select(g => new
            {
                Provider = g.Key,
                Count = g.Count(),
                ExpectedTotal = g.Sum(f => f.ExpectedAmount),
                Oldest = g.Min(f => f.OccurredAt)
            })
            .OrderBy(row => row.Oldest)
            .Take(_options.SampleSize)
            .ToListAsync(ct);

        return overdue
            .Select(row => new OverdueFees(row.Provider, row.Count, row.ExpectedTotal, row.Oldest))
            .ToArray();
    }
}

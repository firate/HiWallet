namespace HiWallet.WalletService.Infrastructure.Jobs;

public sealed class ReconciliationOptions
{
    public const string SectionName = "Jobs:Reconciliation";

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Bir top-up'ın settlement'ı bu süreyi aşarsa raporlanıyor.
    ///
    /// Sağlayıcılar tipik olarak 2-3 iş günü içinde ödüyor; eşik hafta sonunu da
    /// kapsayacak kadar cömert. Dar tutmak her hafta sonu yanlış alarm üretir ve
    /// alarmın değerini düşürür.
    /// </summary>
    public TimeSpan SettlementDue { get; init; } = TimeSpan.FromDays(5);

    /// <summary>
    /// Faturalanmamış ücretin bu yaşı aşması gecikme sayılıyor. Settlement'tan
    /// uzun: fatura dönem sonunda kesiliyor, yani bir aylık gecikme normal.
    /// </summary>
    public TimeSpan InvoiceDue { get; init; } = TimeSpan.FromDays(45);

    /// <summary>Raporda listelenecek en fazla kalem. Alarm satırı okunabilir kalsın.</summary>
    public int SampleSize { get; init; } = 20;
}

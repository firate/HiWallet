namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// İnceleme eşiği, para birimi başına. Tutarı eşiğin ÜSTÜNDE olan çekim cüzdandan
/// düşüldükten sonra bankaya gitmiyor, operasyon rolünden bir çalışanın kararını
/// bekliyor. Eşiği tanımlı olmayan para biriminde çekim incelemeye girmiyor.
///
/// Bölüm eksikse uygulama açılmıyor: sessizce incelemesiz çalışmamalı.
/// </summary>
public sealed class WithdrawalReviewOptions
{
    public const string SectionName = "Withdrawals:Review";

    /// <summary>Anahtar ISO 4217 kodu (<c>TRY</c>), değer eşik.</summary>
    public Dictionary<string, decimal> Above { get; set; } = [];

    public bool Requires(string currency, decimal amount) =>
        Above.TryGetValue(currency, out var threshold) && amount > threshold;
}

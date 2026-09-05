using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>
/// Tip başına komisyon tarifesi. Değerler konfigürasyondan gelir; Domain
/// <c>IConfiguration</c> tanımaz, bağlama Setup katmanının işi.
/// </summary>
/// <param name="Rate">Oran (0.02 = %2).</param>
/// <param name="Minimum">Oran bu tutarın altında kalırsa bu uygulanır. Yoksa <c>null</c>.</param>
/// <param name="Maximum">Tavan. Yoksa <c>null</c>.</param>
public sealed record CommissionRate(decimal Rate, decimal? Minimum = null, decimal? Maximum = null)
{
    public static CommissionRate None { get; } = new(0m);

    /// <summary>
    /// Tarifeyi tutara uygular. Yuvarlama kararı BURADA, tek yerde: transfer ve çekim
    /// aynı hesabı yapıyor ve iki kopya olsaydı biri değiştiğinde müşteri aynı oranı
    /// iki akışta farklı ödemeye başlardı — hiçbir test bunu kendiliğinden yakalamaz.
    ///
    /// Dönen değer her zaman pozitif ya da sıfır; işaret ledger'a yazılırken veriliyor.
    /// </summary>
    public Money Apply(Money amount)
    {
        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Tutar pozitif olmalı.", nameof(amount));
        }

        if (Rate <= 0m) return Money.Zero(amount.Currency);

        // Varsayım (kural docs'ta yok): komisyon yukarı değil, en yakına yuvarlanır ve
        // 0.5'te sıfırdan uzağa gidilir — banker's rounding binlerce işlemde kurumun
        // lehine sistematik sapma üretmiyor ama "yarımı aşağı yuvarladık" tartışması
        // açıyor. AwayFromZero müşteri açısından öngörülebilir.
        var rounded = decimal.Round(
            amount.Amount * Rate, amount.Currency.MinorUnit, MidpointRounding.AwayFromZero);

        if (Minimum is { } min && rounded < min) rounded = min;
        if (Maximum is { } max && rounded > max) rounded = max;

        return new Money(rounded, amount.Currency);
    }
}

/// <summary>
/// Komisyon hesabı. Saf hesaplama — DB'ye dokunmaz.
///
/// Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağıdır (overview.md madde 4):
/// gönderenden düşülür, <c>revenue</c> hesabına credit yazılır.
/// </summary>
public sealed class CommissionPolicy
{
    private readonly IReadOnlyDictionary<TransferType, CommissionRate> _rates;

    public CommissionPolicy(IReadOnlyDictionary<TransferType, CommissionRate> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        _rates = rates;
    }

    /// <summary>
    /// Transfer tutarı üzerinden kesilecek komisyon. Sıfır olabilir (p2p'de tarife yok).
    /// Dönen değer her zaman pozitif ya da sıfır — işaret ledger'a yazılırken verilir.
    /// </summary>
    public Money Calculate(TransferType type, Money amount)
    {
        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Transfer tutarı pozitif olmalı.", nameof(amount));
        }

        return _rates.TryGetValue(type, out var rate)
            ? rate.Apply(amount)
            : Money.Zero(amount.Currency);
    }
}

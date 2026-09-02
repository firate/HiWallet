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
}

/// <summary>
/// Komisyon hesabı. Saf hesaplama — DB'ye dokunmaz.
///
/// Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağıdır (Bölüm 3 madde 4):
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

        if (!_rates.TryGetValue(type, out var rate) || rate.Rate <= 0m)
        {
            return Money.Zero(amount.Currency);
        }

        var raw = amount.Amount * rate.Rate;

        // Varsayım (kural docs'ta yok): komisyon yukarı değil, en yakına yuvarlanır ve
        // 0.5'te sıfırdan uzağa gidilir — banker's rounding binlerce işlemde kurumun
        // lehine sistematik sapma üretmiyor ama "yarımı aşağı yuvarladık" tartışması
        // açıyor. AwayFromZero müşteri açısından öngörülebilir.
        var rounded = decimal.Round(raw, amount.Currency.MinorUnit, MidpointRounding.AwayFromZero);

        if (rate.Minimum is { } min && rounded < min)
        {
            rounded = min;
        }

        if (rate.Maximum is { } max && rounded > max)
        {
            rounded = max;
        }

        return new Money(rounded, amount.Currency);
    }
}

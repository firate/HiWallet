using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>
/// Tip başına limit tarifesi. Değerler konfigürasyondan gelir.
/// </summary>
/// <param name="PerTransaction">Tek işlem tavanı. Yoksa <c>null</c>.</param>
/// <param name="Daily">Günlük toplam tavanı. Yoksa <c>null</c>.</param>
public sealed record TransferLimit(decimal? PerTransaction = null, decimal? Daily = null)
{
    public static TransferLimit Unlimited { get; } = new();
}

/// <summary>
/// Limit kontrolü. Saf hesaplama — günlük harcamayı DB'den okumak çağıranın işi.
///
/// Transfer'den ÖNCE, aynı transaction'ın parçası olarak çalışır; aşılırsa transfer
/// hiç başlamaz. Limit aşımı bir iş kuralı reddidir, hata değil → <c>422</c> (Bölüm 3 §4).
/// </summary>
public sealed class LimitPolicy
{
    private readonly IReadOnlyDictionary<TransferType, TransferLimit> _limits;

    public LimitPolicy(IReadOnlyDictionary<TransferType, TransferLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

    /// <summary>
    /// Aşım varsa <see cref="LimitExceededException"/> fırlatır, yoksa sessizce döner.
    /// </summary>
    /// <param name="amount">
    /// Kontrol edilen tutar. Komisyon DAHİL değil — limit müşterinin gönderdiği tutara
    /// uygulanır, kurumun kestiği komisyona değil. (Varsayım: kural docs'ta yok.)
    /// </param>
    /// <param name="spentToday">Aynı tip için bugün gerçekleşmiş toplam.</param>
    public void Ensure(Guid accountId, TransferType type, Money amount, Money spentToday)
    {
        if (!_limits.TryGetValue(type, out var limit))
        {
            return;
        }

        if (limit.PerTransaction is { } perTransaction && amount.Amount > perTransaction)
        {
            throw new LimitExceededException(
                accountId, $"{type}.PerTransaction", new Money(perTransaction, amount.Currency), amount);
        }

        if (limit.Daily is { } daily)
        {
            var projected = spentToday + amount;

            if (projected.Amount > daily)
            {
                throw new LimitExceededException(
                    accountId, $"{type}.Daily", new Money(daily, amount.Currency), projected);
            }
        }
    }
}

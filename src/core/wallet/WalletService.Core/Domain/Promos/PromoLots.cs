namespace HiWallet.WalletService.Domain.Promos;

/// <summary>
/// Ödeme anında harcanabilir bir promo partisi.
/// </summary>
/// <param name="Remaining">Partinin tutarından tüketimleri düşülmüş kalanı.</param>
/// <param name="Restricted">Seçili işyerleriyle kısıtlı mı.</param>
public readonly record struct PromoLot(
    Guid GrantId, decimal Remaining, DateTimeOffset? ExpiresAt, bool Restricted, DateTimeOffset CreatedAt);

/// <summary>Bir partiden tüketilen tutar.</summary>
public readonly record struct PromoTake(Guid GrantId, decimal Amount);

/// <summary>
/// Partilerin tüketim sırası (decisions.md madde 37). Saf — DB ve zaman bilmez.
///
/// <b>Sıra sabit ve konfigüre EDİLMEZ:</b> bitişi en yakın olan önce (süresiz en
/// sonda), bitiş eşitse kısıtlı olan, o da eşitse eski olan. İlk kural müşterinin
/// süre sonunda kaybettiği tutarı en aza indiriyor, ikincisi her yerde geçerli
/// promo'yu başka işyerleri için saklıyor.
/// </summary>
public static class PromoLots
{
    /// <summary>
    /// En fazla <paramref name="max"/> kadar tüketir. Partiler yetmezse hepsini
    /// tüketir; eksik kalan çağıranın işi.
    /// </summary>
    public static IReadOnlyList<PromoTake> Take(IEnumerable<PromoLot> lots, decimal max)
    {
        var takes = new List<PromoTake>();

        foreach (var lot in lots
                     .Where(l => l.Remaining > 0m)
                     .OrderBy(l => l.ExpiresAt is null)
                     .ThenBy(l => l.ExpiresAt)
                     .ThenBy(l => !l.Restricted)
                     .ThenBy(l => l.CreatedAt)
                     .ThenBy(l => l.GrantId))
        {
            if (max <= 0m) break;

            var take = Math.Min(lot.Remaining, max);

            takes.Add(new PromoTake(lot.GrantId, take));
            max -= take;
        }

        return takes;
    }
}

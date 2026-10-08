using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>Seviye limitinin sayıldığı hareket.</summary>
public enum KycMovement
{
    /// <summary>Başka bir hesaptan gelen transfer (P2P, B2P).</summary>
    IncomingTransfer,

    /// <summary>Başka bir hesaba giden transfer (P2P, P2B).</summary>
    OutgoingTransfer,

    /// <summary>İşyerine ödeme.</summary>
    Payment,

    /// <summary>Bankaya çekim.</summary>
    Withdrawal,

    /// <summary>
    /// Yükleme: havale ya da kart. Limitle kesilen yalnızca havale; kartın yükleme başlatma
    /// adımı yok, para sağlayıcıdan çekildikten sonra haber geliyor. Ama sayımda kart da
    /// var: ikisi de hesaba para yüklüyor.
    /// </summary>
    Deposit,

    /// <summary>
    /// Hesaba giren TOPLAM: yükleme ve gelen transfer birlikte. Kimliği tespit edilmemiş
    /// müşteride yasal sınır ayın toplam yüklemesi üzerinde (MASAK Genel Tebliği Sıra No 5,
    /// 2.2.11). İki hareket yalnızca ayrı ayrı sınırlansaydı ay içinde harcanan para tavanın
    /// iki katını hesaba sokardı.
    /// </summary>
    IncomingTotal
}

/// <summary>Hesaba bu ay giren: yüklemeler ve başka hesaplardan gelen transferler.</summary>
public sealed record IncomingThisMonth(Money Deposits, Money Transfers);

/// <summary>
/// Doğrulama seviyesine göre AYLIK limitler ve bakiye tavanı. Günlük ve işlem başına
/// tarifenin (<see cref="LimitPolicy"/>) yerine değil üstüne: o ürünün risk sınırı, bu
/// seviyenin izin verdiği hacim. Yalnızca bireysel hesaba uygulanıyor.
///
/// Tarifede olmayan seviye ya da hareket KAPALI sayılıyor: eksik bir satır doğrulanmamış
/// bir hesabı sınırsız bırakmamalı. Uygulama açılırken tarifenin tam olduğu ayrıca
/// kontrol ediliyor.
///
/// Bakiye tavanı yalnızca kimliği tespit edilmemiş seviyelerde
/// (<see cref="KycLevels.IsIdentified"/>): yasal sınır o müşterinin bakiyesini de her an
/// kesiyor. Tavanı tanımlı olmayan seviyede tavan yok; tespit edilmemiş seviyenin tavanı
/// uygulama açılırken zorunlu tutuluyor.
///
/// Saf hesaplama; ayın toplamını ve bakiyeyi okumak çağıranın işi. Kapsam hesap, cüzdan
/// değil: aksi halde ikinci cüzdan açılarak aşılırdı (decisions.md madde 20).
/// </summary>
public sealed class KycLimitPolicy(
    IReadOnlyDictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>> monthly,
    IReadOnlyDictionary<KycLevel, decimal> balanceCaps)
{
    private const string BalanceCapName = "Kyc.Balance";

    /// <summary>Hesaptan çıkan hareket. Tutar cüzdandan çıkan toplam, komisyon dahil (madde 22).</summary>
    public void EnsureOutgoing(
        Guid accountId, KycLevel level, KycMovement movement, Money amount, Money spentThisMonth)
    {
        var limit = Limit(level, movement);
        var projected = spentThisMonth + amount;

        if (projected.Amount > limit)
        {
            throw new LimitExceededException(
                accountId, Name(movement), new Money(limit, amount.Currency), projected);
        }
    }

    /// <summary>
    /// Hesaba gelen hareket: hareketin kendi aylık limiti, ayın toplam girişi ve bakiye
    /// tavanı, bu sırayla. Reddi gönderen görüyor, hata alıcıyı söylemiyor.
    /// </summary>
    /// <param name="balance">Hesabın bu para birimindeki bütün cüzdanlarının toplamı.</param>
    public void EnsureIncoming(
        KycLevel level, KycMovement movement, Money amount, IncomingThisMonth received, Money balance)
    {
        var sameKind = movement switch
        {
            KycMovement.IncomingTransfer => received.Transfers,
            KycMovement.Deposit => received.Deposits,
            _ => throw new ArgumentException($"{movement} hesaba gelen bir hareket değil.", nameof(movement))
        };

        Ensure(level, movement, amount, sameKind);
        Ensure(level, KycMovement.IncomingTotal, amount, received.Deposits + received.Transfers);

        if (balanceCaps.TryGetValue(level, out var cap) && (balance + amount).Amount > cap)
        {
            throw new IncomingLimitExceededException(BalanceCapName, new Money(cap, amount.Currency));
        }
    }

    /// <summary>Seviyenin bu hareketteki aylık limiti; tarifede yoksa sıfır, yani kapalı.</summary>
    public decimal MonthlyLimit(KycLevel level, KycMovement movement) => Limit(level, movement);

    /// <summary>Seviyenin bakiye tavanı; kimliği tespit edilmiş seviyede yok.</summary>
    public decimal? BalanceCap(KycLevel level) => balanceCaps.TryGetValue(level, out var cap) ? cap : null;

    private void Ensure(KycLevel level, KycMovement movement, Money amount, Money receivedThisMonth)
    {
        var limit = Limit(level, movement);

        if ((receivedThisMonth + amount).Amount > limit)
        {
            throw new IncomingLimitExceededException(Name(movement), new Money(limit, amount.Currency));
        }
    }

    private decimal Limit(KycLevel level, KycMovement movement) =>
        monthly.TryGetValue(level, out var limits) && limits.TryGetValue(movement, out var limit)
            ? limit
            : 0m;

    private static string Name(KycMovement movement) => $"Kyc.{movement}.Monthly";
}

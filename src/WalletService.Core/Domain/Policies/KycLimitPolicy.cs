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
    Withdrawal
}

/// <summary>
/// Doğrulama seviyesine göre AYLIK limitler. Günlük ve işlem başına tarifenin
/// (<see cref="LimitPolicy"/>) yerine değil üstüne: o ürünün risk sınırı, bu seviyenin
/// izin verdiği hacim. Yalnızca bireysel hesaba uygulanıyor.
///
/// Tarifede olmayan seviye ya da hareket KAPALI sayılıyor: eksik bir satır doğrulanmamış
/// bir hesabı sınırsız bırakmamalı. Uygulama açılırken tarifenin tam olduğu ayrıca
/// kontrol ediliyor.
///
/// Saf hesaplama; ayın toplamını okumak çağıranın işi. Kapsam hesap, cüzdan değil: aksi
/// halde ikinci cüzdan açılarak aşılırdı (decisions.md madde 20).
/// </summary>
public sealed class KycLimitPolicy(IReadOnlyDictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>> monthly)
{
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

    /// <summary>Hesaba gelen hareket. Reddi gönderen görüyor, hata alıcıyı söylemiyor.</summary>
    public void EnsureIncoming(KycLevel level, KycMovement movement, Money amount, Money receivedThisMonth)
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

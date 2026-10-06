using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.CardTopups;

/// <summary>
/// Kartla yüklemenin seviye limitinden ayırdığı pay. Ödeme başlarken yazılıyor; ödeme
/// kapanana kadar hesaba gelen her para (havale, transfer, başka bir kart yüklemesi) bu
/// payı ayın girişine ve bakiyeye eklenmiş sayıyor. Kart limitte öncelikli: ödeme
/// sürerken limiti dolduracak başka bir para reddediliyor, kart parası geldiğinde yeniden
/// kontrol edilmeden cüzdana yazılıyor.
///
/// Ledger DEĞİL: para henüz hareket etmedi. Satır değişmiyor ve silinmiyor; ödeme
/// kapandığında <see cref="CardTopupHoldClosure"/> ekleniyor. Açık pay, kapanışı olmayan
/// satır.
///
/// <b>Saatle düşmüyor.</b> Pay ancak ödeme kapandığında düşüyor: ödendi, vazgeçildi ya da
/// süresi doldu. Süreyi saat değil sağlayıcı söylüyor; kart yüklemesi servisi cevapsız kalan
/// ödemeyi sağlayıcıya sorarak kapatıyor. Saatle düşseydi ödemenin son anında çekilen kartın
/// bildirimi geciktiğinde pay limitten çıkmış olur, arada gelen başka para tavanı
/// doldururdu ve kartın önceliği delinirdi.
/// </summary>
public sealed class CardTopupHold
{
    private CardTopupHold()
    {
    }

    /// <summary>Kart yüklemesinin kimliği; kart yüklemesi servisi veriyor.</summary>
    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Paranın yazılacağı cüzdan.</summary>
    public Guid WalletId { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Kart sağlayıcısı; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public string Provider { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Money => new(Amount, Currency);
}

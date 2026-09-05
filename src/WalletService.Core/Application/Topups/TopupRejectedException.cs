namespace HiWallet.WalletService.Application.Topups;

/// <summary>
/// Mesaj kalıcı olarak işlenemez: cüzdan yok, para birimi tutmuyor, tutar geçersiz.
/// Yeniden denemek aynı sonucu verir.
///
/// Tüketici bu istisnayı gördüğünde mesajı kuyruğa GERİ KOYMUYOR, dead-letter'a
/// yolluyor. Ayrım kritik: geçici hatada (DB kapalı) mesaj kuyrukta kalmalı, kalıcı
/// hatada kalmamalı — kalırsa <c>x-single-active-consumer</c> ile sıralı işlenen
/// partition'ı süresiz tıkar ve arkasındaki bütün cüzdanları durdurur.
/// </summary>
public sealed class TopupRejectedException(string provider, string eventId, string reason)
    : Exception($"Top-up reddedildi ({provider}/{eventId}): {reason}")
{
    public string Provider { get; } = provider;

    public string EventId { get; } = eventId;
}

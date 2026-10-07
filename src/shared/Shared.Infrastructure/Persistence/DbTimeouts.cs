namespace HiWallet.Shared.Infrastructure.Persistence;

/// <summary>
/// Veritabanı çağrılarının zaman aşımı (<c>baseline.md</c> madde 11). Dört
/// <c>DbContext</c> de aynı değeri kullanıyor; servis başına ayrı yazılsaydı biri
/// değiştiğinde diğerleri sessizce eskide kalırdı.
/// </summary>
public static class DbTimeouts
{
    /// <summary>
    /// Komut başına saniye. Npgsql'in varsayılanı 30; bu sistemin sorguları tek
    /// hesaba ya da küçük bir batch'e dokunuyor ve saniyenin altında dönüyor.
    /// 30 saniye takılan bir sorgunun bağlantıyı ve request'i o kadar tutması
    /// demek — bir bağımlılığın yavaşlaması tüm servisi kilitler (madde 11).
    ///
    /// Migration'lar BU DEĞERİ GÖRMÜYOR: onlar <c>--connection</c> ile design-time
    /// factory üzerinden koşuyor, uygulamanın kaydı devrede değil. Büyük bir şema
    /// değişikliği bu yüzden kesilmiyor.
    /// </summary>
    public const int CommandSeconds = 10;
}

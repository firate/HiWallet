using System.ComponentModel.DataAnnotations;

namespace HiWallet.Shared.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    /// <summary>
    /// <c>amqp://kullanici:parola@host:5672/</c>. Parola içerdiği için konfigürasyondan
    /// değil ortam değişkeninden gelmesi beklenir.
    /// </summary>
    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Kaç partition kuyruğu açılacağı. Aynı cüzdanın mesajları hep aynı kuyruğa
    /// düşer, sıra orada korunur (overview.md madde 8).
    ///
    /// DEĞİŞTİRİLEMEZ bir sayı gibi düşünülmeli: sonradan artırmak bir cüzdanın
    /// mesajlarını başka kuyruğa taşır ve eski kuyrukta bekleyen mesaj varsa
    /// sıralama garantisi o cüzdan için tek seferlik bozulur. Artırmadan önce
    /// kuyrukların boşalması beklenir.
    /// </summary>
    [Range(1, 64)]
    public int PartitionCount { get; init; } = 4;

    /// <summary>
    /// Broker yönetim arayüzünde bağlantıyı kimin açtığını görebilmek için.
    /// Konfigürasyondan değil, servisin kendi kaydından geliyor.
    /// </summary>
    public string ClientName { get; set; } = "hiwallet";
}

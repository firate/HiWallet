namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Broker bağlantısı. Tek bir <c>amqp://kullanici:parola@host</c> dizesi yerine ayrı
/// alanlar: parola URL'in içine gömülmüş olsaydı her log satırında, her hata
/// mesajında ve her konfigürasyon dökümünde onunla birlikte dolaşırdı.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 5672;

    public string Username { get; init; } = string.Empty;

    /// <summary>Konfigürasyondan değil, ortam değişkeni / User Secrets'tan gelir.</summary>
    public string Password { get; init; } = string.Empty;

    public string VirtualHost { get; init; } = "/";

    /// <summary>
    /// Exchange ve kuyruk adlarının önüne eklenir. Canlıda boş.
    ///
    /// Var olma sebebi test izolasyonu: aynı broker'a bakan iki koşu ön eksiz
    /// çalışsaydı biri diğerinin kuyruğundan mesaj çeker ve testler birbirini
    /// rastgele düşürürdü. Aynı mekanizma tek broker'ı paylaşan ortamlar için de
    /// işe yarıyor.
    /// </summary>
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Broker yönetim arayüzünde bağlantıyı kimin açtığını görebilmek için.
    /// Konfigürasyondan değil, servisin kendi kaydından geliyor.
    /// </summary>
    public string ClientName { get; set; } = "hiwallet";
}

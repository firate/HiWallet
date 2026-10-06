namespace HiWallet.EdgeApi.Sessions;

/// <summary>BFF'in kimlik sağlayıcıdaki istemcisi ve oturum anahtarlarının yeri.</summary>
public sealed class SessionSettings
{
    public const string SectionName = "Authentication";

    /// <summary>Kimlik sağlayıcının tarayıcının gördüğü adresi; token'daki <c>iss</c>.</summary>
    public string? Issuer { get; set; }

    /// <summary>Discovery adresi. İç ağdan farklı bir adresle ulaşılıyorsa burada veriliyor.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Canlıda kapatılmaz. Compose'da kimlik sağlayıcı düz HTTP ile konuşuyor.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public string? ClientId { get; set; }

    /// <summary>Ortamdan gelir; dosyaya yazılmaz.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Cookie'yi şifreleyen anahtarların dizini. Verilmezse anahtarlar bellekte kalıyor
    /// ve yeniden başlatınca bütün oturumlar kapanıyor. Birden fazla instance aynı
    /// dizini paylaşmak zorunda: birinin yazdığı cookie'yi öbürü çözemezdi.
    /// </summary>
    public string? KeysDirectory { get; set; }
}

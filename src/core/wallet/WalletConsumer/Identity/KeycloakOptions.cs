namespace HiWallet.WalletConsumer.Identity;

/// <summary>
/// Servisin kendi token'ı için kimlik sağlayıcı. İstemci (<c>wallet-consumer</c>) gizli
/// anahtarlı; token'ının hedef kitlesinde iç servisler (<c>hiwallet-api</c>) var ve
/// onboarding istemcinin adını <c>azp</c>'de arıyor.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>İç ağdaki adres, örneğin <c>http://hiwallet-keycloak:8080</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

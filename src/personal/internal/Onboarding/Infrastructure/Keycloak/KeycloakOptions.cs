namespace HiWallet.Onboarding.Infrastructure.Keycloak;

/// <summary>
/// Kimlik sağlayıcıya iç ağdan erişim. Bu servisin istemcisi (<c>onboarding</c>) gizli
/// anahtarlı; servis hesabının realm'de kullanıcı açma yetkisi var ve token'ının hedef
/// kitlesinde iç servisler (<c>hiwallet-api</c>) var.
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

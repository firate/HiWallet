namespace HiWallet.StaffAdmin.Infrastructure.Keycloak;

/// <summary>
/// Çalışanların kimlik sağlayıcısına iç ağdan erişim. Bu servisin istemcisi
/// (<c>staff-admin</c>) gizli anahtarlı; servis hesabının yalnızca çalışanların realm'inde
/// kullanıcı ve rol yönetme yetkisi var.
/// </summary>
public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>İç ağdaki adres, örneğin <c>http://hiwallet-staff-keycloak:8080</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public InvitationOptions Invitation { get; set; } = new();

    /// <summary>Davet e-postasındaki bağlantı: parola ve OTP kurulunca çalışan panele dönüyor.</summary>
    public sealed class InvitationOptions
    {
        /// <summary>Dönüşün yapıldığı istemci; panelin istemcisi.</summary>
        public string? ClientId { get; set; }

        /// <summary>Panelin adresi; istemcinin dönüş adresleri arasında olmalı. Boşsa dönüş yok.</summary>
        public string? RedirectUri { get; set; }

        /// <summary>Bağlantının geçerlilik süresi.</summary>
        public int LifespanHours { get; set; } = 72;
    }
}

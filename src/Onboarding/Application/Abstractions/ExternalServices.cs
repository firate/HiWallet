using HiWallet.Onboarding.Domain;

namespace HiWallet.Onboarding.Application.Abstractions;

/// <summary>Kimlik sağlayıcıdaki kullanıcı.</summary>
public sealed record IdentityUser(string Subject, string Email);

/// <summary>
/// Kimlik sağlayıcının kullanıcı yönetimi (Keycloak'ın yönetim API'si). Parola ve oturum
/// orada; bu servis parolayı yalnızca kullanıcıyı açarken geçiriyor, saklamıyor.
/// </summary>
public interface IIdentityProvider
{
    Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>E-postası doğrulanmış kullanıcı açar; kullanıcı adı e-posta.</summary>
    /// <returns>Kullanıcının <c>sub</c>'ı.</returns>
    /// <exception cref="PasswordRejectedException">Parola kimlik sağlayıcının kuralına uymuyor.</exception>
    /// <exception cref="IdentityConflictException">Bu e-postayla bir kullanıcı var.</exception>
    Task<string> CreateUserAsync(string email, string password, CancellationToken ct);

    /// <exception cref="PasswordRejectedException">Parola kimlik sağlayıcının kuralına uymuyor.</exception>
    Task SetPasswordAsync(string subject, string password, CancellationToken ct);
}

public sealed class PasswordRejectedException(string reason) : Exception(reason);

public sealed class IdentityConflictException(string email)
    : Exception("Bu e-postayla kimlik sağlayıcıda bir kullanıcı var.")
{
    public string Email { get; } = email;
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>SMS sağlayıcısı: başka bir kurum.</summary>
public interface ISmsSender
{
    Task SendAsync(PhoneNumber to, string text, CancellationToken ct);
}

/// <summary>
/// Nüfus kaydı: kimlik numarası, ad, soyad ve doğum yılı eşleşiyor mu. Başka bir kurum;
/// yalnızca eşleşip eşleşmediğini söylüyor.
/// </summary>
public interface IPopulationRegistry
{
    Task<bool> MatchesAsync(NationalId nationalId, string firstName, string lastName, int birthYear, CancellationToken ct);
}

/// <summary>
/// Bu servisin kendi token'ı: Keycloak'taki istemcisinin kimlik bilgileriyle (client
/// credentials). Kullanıcı açarken ve wallet'ı çağırırken kullanılıyor.
/// </summary>
public interface IServiceTokens
{
    Task<string> GetAsync(CancellationToken ct);
}

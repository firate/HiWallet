namespace HiWallet.StaffAdmin.Application.Abstractions;

/// <summary>
/// Çalışanların kimlik sağlayıcısındaki kullanıcıları: parola, OTP ve oturum orada.
/// Rol ve izin orada YOK; çalışanın kim olduğu, açık mı ve hangi rollerde olduğu bu
/// servisin veritabanında. Bu servis kullanıcıyı açıyor, davet ediyor ve kapatıyor.
/// </summary>
public interface IStaffDirectory
{
    Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct);

    /// <summary>Kullanıcıyı açar; parolası ve OTP'si yok, onları davetle kendisi kuruyor.</summary>
    /// <exception cref="DirectoryConflictException">Bu e-postayla bir kullanıcı var.</exception>
    Task<Guid> CreateUserAsync(string email, string? firstName, string? lastName, CancellationToken ct);

    /// <summary>Parolayı ve OTP'yi kurduran bağlantıyı e-postayla gönderir.</summary>
    Task SendInvitationAsync(Guid userId, CancellationToken ct);

    /// <summary>Kapatılan kullanıcı giriş yapamıyor; açık oturumları da kapanıyor.</summary>
    Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct);
}

public sealed record DirectoryUser(Guid Id, bool Enabled);

/// <summary>Kimlik sağlayıcıda aynı e-postada kullanıcı var.</summary>
public sealed class DirectoryConflictException(string message) : Exception(message);

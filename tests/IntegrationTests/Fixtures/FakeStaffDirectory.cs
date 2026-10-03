using System.Collections.Concurrent;
using HiWallet.StaffAdmin.Application.Abstractions;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Çalışanların kimlik sağlayıcısı, bellekte. Gerçeğiyle aynı kural: e-posta tekil,
/// büyük-küçük harf farkı gözetmeden. Roller ve izinler burada değil, personel
/// yönetiminin veritabanında.
/// </summary>
public sealed class FakeStaffDirectory : IStaffDirectory
{
    private readonly ConcurrentDictionary<Guid, (string Email, bool Enabled)> _users = new();

    /// <summary>Davet gönderilen kullanıcılar, gönderim sırasıyla.</summary>
    public ConcurrentQueue<Guid> Invitations { get; } = new();

    /// <summary>Kapatılırken oturumu da kapatılan kullanıcılar.</summary>
    public ConcurrentQueue<Guid> SignedOut { get; } = new();

    /// <summary>Kimlik sağlayıcıda bir kullanıcı; personel yönetiminde kaydı yok.</summary>
    public Guid AddUser(string email)
    {
        var id = Guid.NewGuid();
        _users[id] = (email, true);
        return id;
    }

    public bool IsEnabled(Guid userId) => _users[userId].Enabled;

    public Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(_users
            .Where(u => string.Equals(u.Value.Email, email, StringComparison.OrdinalIgnoreCase))
            .Select(u => new DirectoryUser(u.Key, u.Value.Enabled))
            .FirstOrDefault());

    public Task<Guid> CreateUserAsync(string email, string? firstName, string? lastName, CancellationToken ct)
    {
        if (_users.Values.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DirectoryConflictException($"{email} kayıtlı.");
        }

        return Task.FromResult(AddUser(email));
    }

    public Task SendInvitationAsync(Guid userId, CancellationToken ct)
    {
        Invitations.Enqueue(userId);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        _users[userId] = _users[userId] with { Enabled = enabled };

        if (!enabled)
        {
            SignedOut.Enqueue(userId);
        }

        return Task.CompletedTask;
    }
}

using System.Collections.Concurrent;
using HiWallet.StaffAdmin.Application.Abstractions;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Çalışanların kimlik sağlayıcısı, bellekte. Gerçeğiyle aynı kurallar: rol adı ve
/// e-posta tekil (büyük-küçük harf farkı gözetmeden), silinen rolün atamaları da gidiyor.
/// Kimlik sağlayıcının kendi varsayılan rolü baştan var; panel ona dokunmamalı.
/// </summary>
public sealed class FakeStaffDirectory : IStaffDirectory
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, DirectoryRole> _roles = new();
    private readonly Dictionary<Guid, DirectoryUser> _users = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _assignments = new();

    public FakeStaffDirectory()
    {
        DefaultRole = new DirectoryRole(Guid.NewGuid(), "default-roles-hiwallet-staff", null, DirectoryRoleKind.Other, []);
        _roles[DefaultRole.Id] = DefaultRole;
    }

    public DirectoryRole DefaultRole { get; }

    /// <summary>Davet gönderilen kullanıcılar, gönderim sırasıyla.</summary>
    public ConcurrentQueue<Guid> Invitations { get; } = new();

    /// <summary>Kapatılırken oturumu da kapatılan kullanıcılar.</summary>
    public ConcurrentQueue<Guid> SignedOut { get; } = new();

    public DirectoryRole? RoleNamed(string name)
    {
        lock (_lock)
        {
            return _roles.Values.SingleOrDefault(r => r.Name == name);
        }
    }

    public DirectoryUser? UserWithEmail(string email)
    {
        lock (_lock)
        {
            return _users.Values.SingleOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Testin çalışanı: token'daki kimlikle dizinde bir kullanıcı.</summary>
    public Guid AddUser(string email, Guid? id = null)
    {
        lock (_lock)
        {
            var user = new DirectoryUser(id ?? Guid.NewGuid(), email, null, null, true, DateTimeOffset.UtcNow, []);
            _users[user.Id] = user;
            return user.Id;
        }
    }

    public IReadOnlyList<Guid> RolesOf(Guid userId)
    {
        lock (_lock)
        {
            return _assignments.TryGetValue(userId, out var roles) ? [.. roles] : [];
        }
    }

    public Task<IReadOnlyList<DirectoryRole>> ListRolesAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DirectoryRole>>([.. _roles.Values]);
        }
    }

    public Task<DirectoryRole?> FindRoleAsync(Guid roleId, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_roles.GetValueOrDefault(roleId));
        }
    }

    public Task<Guid> CreateRoleAsync(
        string name, string description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        lock (_lock)
        {
            ThrowIfNameTaken(name);
            var role = new DirectoryRole(Guid.NewGuid(), name, description, DirectoryRoleKind.StaffRole, [.. permissions]);
            _roles[role.Id] = role;
            return Task.FromResult(role.Id);
        }
    }

    public Task UpdateRoleAsync(
        Guid roleId, string description, IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        lock (_lock)
        {
            _roles[roleId] = _roles[roleId] with { Description = description, Permissions = [.. permissions] };
            return Task.CompletedTask;
        }
    }

    public Task DeleteRoleAsync(Guid roleId, CancellationToken ct)
    {
        lock (_lock)
        {
            _roles.Remove(roleId);

            foreach (var roles in _assignments.Values)
            {
                roles.Remove(roleId);
            }

            return Task.CompletedTask;
        }
    }

    public Task EnsurePermissionAsync(string permission, string description, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_roles.Values.All(r => r.Name != permission))
            {
                var role = new DirectoryRole(Guid.NewGuid(), permission, description, DirectoryRoleKind.Permission, []);
                _roles[role.Id] = role;
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<DirectoryUser>> RoleMembersAsync(Guid roleId, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DirectoryUser>>(
            [
                .. _assignments.Where(a => a.Value.Contains(roleId)).Select(a => _users[a.Key])
            ]);
        }
    }

    public Task<DirectoryUserPage> ListUsersAsync(int first, int size, string? search, CancellationToken ct)
    {
        lock (_lock)
        {
            var matching = _users.Values
                .Where(u => search is null || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(u => u.Email, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new DirectoryUserPage(
                [.. matching.Skip(first).Take(size)],
                matching.Count > first + size));
        }
    }

    public Task<DirectoryUser?> FindUserAsync(Guid userId, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_users.GetValueOrDefault(userId));
        }
    }

    public Task<DirectoryUser?> FindUserByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(UserWithEmail(email));

    public Task<Guid> CreateUserAsync(string email, string? firstName, string? lastName, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_users.Values.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DirectoryConflictException($"{email} kayıtlı.");
            }

            // Kimlik sağlayıcı yeni kullanıcıya parola ve OTP kurulumunu kendisi ekliyor.
            var user = new DirectoryUser(
                Guid.NewGuid(), email, firstName, lastName, true, DateTimeOffset.UtcNow,
                ["UPDATE_PASSWORD", "CONFIGURE_TOTP"]);
            _users[user.Id] = user;
            return Task.FromResult(user.Id);
        }
    }

    public Task SendInvitationAsync(Guid userId, CancellationToken ct)
    {
        Invitations.Enqueue(userId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> UserRoleIdsAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(RolesOf(userId));

    public Task AssignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_assignments.TryGetValue(userId, out var assigned))
            {
                _assignments[userId] = assigned = [];
            }

            assigned.UnionWith(roles.Select(r => r.Id));
            return Task.CompletedTask;
        }
    }

    public Task UnassignRolesAsync(Guid userId, IReadOnlyCollection<DirectoryRole> roles, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_assignments.TryGetValue(userId, out var assigned))
            {
                assigned.ExceptWith(roles.Select(r => r.Id));
            }

            return Task.CompletedTask;
        }
    }

    public Task SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        lock (_lock)
        {
            _users[userId] = _users[userId] with { Enabled = enabled };
        }

        if (!enabled)
        {
            SignedOut.Enqueue(userId);
        }

        return Task.CompletedTask;
    }

    private void ThrowIfNameTaken(string name)
    {
        if (_roles.Values.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DirectoryConflictException($"{name} adında bir rol var.");
        }
    }
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Keycloak'ın yönetim API'sinin yerine: kullanıcıları bellekte tutuyor. E-posta
/// tekrarını Keycloak gibi reddediyor (realm'de <c>duplicateEmailsAllowed: false</c>).
/// </summary>
public sealed class FakeIdentityProvider : IIdentityProvider
{
    public const int MinPasswordLength = 8;

    private readonly ConcurrentDictionary<string, (string Subject, string Password)> _byEmail = new();

    public IReadOnlyDictionary<string, (string Subject, string Password)> Users => _byEmail;

    /// <summary>Kaydı yarıda kalmış bir kullanıcı: Keycloak'ta açılmış ama kayda bağlanmamış.</summary>
    public string Seed(string email, string password)
    {
        var subject = Guid.NewGuid().ToString();
        _byEmail[email] = (subject, password);
        return subject;
    }

    public Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(_byEmail.TryGetValue(email, out var user) ? new IdentityUser(user.Subject, email) : null);

    public Task<string> CreateUserAsync(string email, string password, CancellationToken ct)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new PasswordRejectedException("Parola en az 8 karakter olmalı.");
        }

        var subject = Guid.NewGuid().ToString();

        if (!_byEmail.TryAdd(email, (subject, password)))
        {
            throw new IdentityConflictException(email);
        }

        return Task.FromResult(subject);
    }

    public Task SetPasswordAsync(string subject, string password, CancellationToken ct)
    {
        var entry = _byEmail.Single(u => u.Value.Subject == subject);
        _byEmail[entry.Key] = (subject, password);
        return Task.CompletedTask;
    }
}

/// <summary>Gönderilen e-postaları tutuyor; test kodu buradan okuyor.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string To, string Subject, string Body)> _sent = new();

    public IReadOnlyCollection<(string To, string Subject, string Body)> Sent => _sent;

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        _sent.Enqueue((to, subject, body));
        return Task.CompletedTask;
    }

    public string LastCodeFor(string to) =>
        Code().Match(_sent.Last(m => m.To == to).Body).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex Code();
}

/// <summary>SMS sağlayıcısının yerine: gönderilen mesajları tutuyor.</summary>
public sealed partial class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string To, string Text)> _sent = new();

    public IReadOnlyCollection<(string To, string Text)> Sent => _sent;

    public Task SendAsync(PhoneNumber to, string text, CancellationToken ct)
    {
        _sent.Enqueue((to.Value, text));
        return Task.CompletedTask;
    }

    public string LastCodeFor(string to) => Code().Match(_sent.Last(m => m.To == to).Text).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex Code();
}

/// <summary>Nüfus kaydının yerine: eşleşmeyecek numaralar önceden söyleniyor.</summary>
public sealed class ScriptedPopulationRegistry : IPopulationRegistry
{
    private readonly ConcurrentDictionary<string, bool> _mismatches = new();

    public void Mismatch(string nationalId) => _mismatches[nationalId] = true;

    public Task<bool> MatchesAsync(
        NationalId nationalId, string firstName, string lastName, int birthYear, CancellationToken ct) =>
        Task.FromResult(!_mismatches.ContainsKey(nationalId.Value));
}

/// <summary>Onboarding'in servis token'ı: Keycloak yerine test anahtarıyla imzalı.</summary>
public sealed class TestServiceTokens : IServiceTokens
{
    public Task<string> GetAsync(CancellationToken ct) =>
        Task.FromResult(TestTokens.For(
            $"service-account-{TestTokens.OnboardingClientId}",
            audiences: [TestTokens.InternalAudience],
            authorizedParty: TestTokens.OnboardingClientId));
}

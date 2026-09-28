using System.Net.Http.Json;
using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;

namespace HiWallet.Onboarding.Infrastructure.PopulationRegistry;

/// <summary>
/// Nüfus kaydının kimlik doğrulama servisi. Yalnızca eşleşip eşleşmediğini söylüyor;
/// kişinin başka bilgisi dönmüyor.
/// </summary>
internal sealed class PopulationRegistryClient(HttpClient http) : IPopulationRegistry
{
    public async Task<bool> MatchesAsync(
        NationalId nationalId, string firstName, string lastName, int birthYear, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            "v1/identity-checks", new IdentityCheckRequest(nationalId.Value, firstName, lastName, birthYear), ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<IdentityCheckResponse>(ct)
                   ?? throw new InvalidOperationException("Nüfus kaydı boş cevap döndü.");

        return body.Matches;
    }

    private sealed record IdentityCheckRequest(string NationalId, string FirstName, string LastName, int BirthYear);

    private sealed record IdentityCheckResponse(bool Matches);
}

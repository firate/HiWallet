using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Nvi.Fake;

public enum IdentityOutcome
{
    Match,
    Mismatch
}

/// <summary>
/// İstek tipleri burada, onboarding'deki istemcide ayrı yazılıyor: gerçek entegrasyonda
/// nüfus kaydının dokümanından geliyorlar.
/// </summary>
public sealed record IdentityCheckRequest(string NationalId, string FirstName, string LastName, int BirthYear);

public sealed record IdentityCheckResponse(bool Matches);

/// <param name="Outcome">Bu numara için dönecek sonuç.</param>
public sealed record ScenarioRequest(string NationalId, IdentityOutcome Outcome);

[ApiController]
[Route("v1")]
public sealed class IdentityChecksController(ConcurrentDictionary<string, IdentityOutcome> scenarios) : ControllerBase
{
    /// <summary>
    /// Kimlik numarası, ad, soyad ve doğum yılı eşleşiyor mu. Gerçek servis yalnızca
    /// evet ya da hayır diyor; kişinin başka bilgisini dönmüyor.
    /// </summary>
    [HttpPost("identity-checks")]
    public IdentityCheckResponse Check([FromBody] IdentityCheckRequest request) =>
        new(!scenarios.TryGetValue(request.NationalId, out var outcome) || outcome is IdentityOutcome.Match);

    /// <summary>Bir numaranın sonucunu belirler: eşleşmeyen kimlik denemek için.</summary>
    [HttpPost("scenarios")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Scenario([FromBody] ScenarioRequest request)
    {
        scenarios[request.NationalId] = request.Outcome;
        return NoContent();
    }
}

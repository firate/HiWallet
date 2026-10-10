using HiWallet.StaffAdmin.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.StaffAdmin.Api.Controllers;

/// <summary>
/// Çalışanların adları: panel bir kaydı kimin açtığını ya da kapattığını adıyla gösteriyor.
/// Varsayılan politika: her çalışanın token'ı yeter, izin istenmiyor. Yalnızca ad dönüyor;
/// rolleri, izinleri ve durumu <c>staff.manage</c> isteyen uçlarda.
/// </summary>
[ApiController]
[Route("v1/staff-names")]
public sealed class StaffNamesController(IDbContextFactory<StaffAdminDbContext> contexts) : ControllerBase
{
    /// <summary>Bir istekte sorulabilecek en çok kimlik: bir sayfadaki kayıtların açanları.</summary>
    public const int MaxSubjects = 100;

    /// <summary>
    /// Kimliklerin adları. Adı girilmemiş çalışanın adı e-postası; panelden açılmamış kimlik
    /// listede yok.
    /// </summary>
    /// <param name="subjects">Token'daki <c>sub</c>'lar; en çok <see cref="MaxSubjects"/>.</param>
    [HttpGet]
    [ProducesResponseType<StaffNamesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StaffNamesResponse>> Get(
        [FromQuery(Name = "subject")] Guid[] subjects, CancellationToken ct)
    {
        var ids = subjects.Distinct().ToArray();

        if (ids.Length > MaxSubjects)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["subject"] = [$"Bir istekte en çok {MaxSubjects} kimlik sorulur."]
                }));
        }

        await using var db = await contexts.CreateDbContextAsync(ct);

        var members = await db.Members
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.FirstName, m.LastName, m.Email })
            .ToListAsync(ct);

        return new StaffNamesResponse(
            [.. members.Select(m => new StaffNameResponse(m.Id.ToString(), DisplayName(m.FirstName, m.LastName, m.Email)))]);
    }

    private static string DisplayName(string? firstName, string? lastName, string email)
    {
        var name = string.Join(' ', new[] { firstName, lastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

        return name.Length > 0 ? name : email;
    }
}

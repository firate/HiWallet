using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application;
using HiWallet.StaffAdmin.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.StaffAdmin.Api.Controllers;

/// <summary>Çalışanlar: davet, rol atama, kapatma. Çalışan kendi hesabında bunları yapamıyor.</summary>
[ApiController]
[Route("v1/staff")]
[Authorize(Policy = HiWalletPolicies.StaffManage)]
public sealed class StaffController(StaffService staff) : ControllerBase
{
    /// <param name="first">Atlanacak çalışan sayısı; önceki sayfanın <c>nextFirst</c> değeri.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    /// <param name="search">E-posta, ad ya da soyadında geçen metin.</param>
    [HttpGet]
    public async Task<StaffPageResponse> List(
        [FromQuery] int? first, [FromQuery] int? size, [FromQuery] string? search, CancellationToken ct)
    {
        var (skip, take) = (Paging.First(first), Paging.Size(size));
        var (items, hasMore) = await staff.ListAsync(skip, take, search, ct);

        return new StaffPageResponse([.. items.Select(ToSummary)], skip, take, hasMore ? skip + take : null);
    }

    [HttpGet("{staffId:guid}")]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<StaffDetailResponse> GetById(Guid staffId, CancellationToken ct) =>
        ToDetail(await staff.GetAsync(staffId, ct));

    /// <summary>
    /// Çalışanı açar, rollerini verir ve davet e-postasını gönderir. Parolasını ve OTP'sini
    /// çalışan davetteki bağlantıdan kendisi kuruyor.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Invite([FromBody] InviteStaffRequest request, CancellationToken ct)
    {
        var detail = await staff.InviteAsync(
            User.ToActor(), request.Email.Trim(), request.FirstName?.Trim(), request.LastName?.Trim(), request.RoleIds, ct);

        return CreatedAtAction(nameof(GetById), new { staffId = detail.Member.Id }, ToDetail(detail));
    }

    /// <summary>Çalışanın rollerinin tamamı; listede olmayanlar alınıyor.</summary>
    [HttpPut("{staffId:guid}/roles")]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StaffDetailResponse> SetRoles(
        Guid staffId, [FromBody] SetStaffRolesRequest request, CancellationToken ct) =>
        ToDetail(await staff.SetRolesAsync(User.ToActor(), staffId, request.RoleIds, ct));

    /// <summary>Çalışanı kapatır; açık oturumları da kapanıyor.</summary>
    [HttpPost("{staffId:guid}/disable")]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StaffDetailResponse> Disable(Guid staffId, CancellationToken ct) =>
        ToDetail(await staff.SetEnabledAsync(User.ToActor(), staffId, enabled: false, ct));

    [HttpPost("{staffId:guid}/enable")]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StaffDetailResponse> Enable(Guid staffId, CancellationToken ct) =>
        ToDetail(await staff.SetEnabledAsync(User.ToActor(), staffId, enabled: true, ct));

    /// <summary>Daveti yeniden gönderir: süresi dolan ya da kaybolan bağlantı için.</summary>
    [HttpPost("{staffId:guid}/invitation")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvitation(Guid staffId, CancellationToken ct)
    {
        await staff.ResendInvitationAsync(User.ToActor(), staffId, ct);

        return Accepted();
    }

    internal static StaffSummaryResponse ToSummary(StaffMember member) => new(
        member.Id, member.Email, member.FirstName, member.LastName, member.Enabled, member.ActivatedAt is null,
        member.CreatedAt);

    private static StaffDetailResponse ToDetail(StaffDetail detail) => new(
        detail.Member.Id,
        detail.Member.Email,
        detail.Member.FirstName,
        detail.Member.LastName,
        detail.Member.Enabled,
        detail.Member.ActivatedAt is null,
        detail.Member.CreatedAt,
        [.. detail.Roles.Select(r => new StaffRoleRef(r.Id, r.Name))]);
}

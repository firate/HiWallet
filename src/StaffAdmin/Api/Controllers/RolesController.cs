using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application;
using HiWallet.StaffAdmin.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.StaffAdmin.Api.Controllers;

/// <summary>Kodun izinleri. Panel rol tanımlarken bunlardan seçiyor.</summary>
[ApiController]
[Route("v1/permissions")]
[Authorize(Policy = HiWalletPolicies.StaffManage)]
public sealed class PermissionsController : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<PermissionResponse> List() =>
        [.. StaffPermissions.All.Select(p => new PermissionResponse(p, StaffPermissions.Descriptions[p]))];
}

/// <summary>Panelin rolleri: izin setleri. Ad sonradan değişmiyor; açıklama ve izinler değişiyor.</summary>
[ApiController]
[Route("v1/roles")]
[Authorize(Policy = HiWalletPolicies.StaffManage)]
public sealed class RolesController(RoleService roles) : ControllerBase
{
    /// <summary>Panelin rolleri, ada göre. İzinler ve kimlik sağlayıcının kendi rolleri listede yok.</summary>
    /// <param name="first">Atlanacak rol sayısı; önceki sayfanın <c>nextFirst</c> değeri.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    public async Task<RolesResponse> List([FromQuery] int? first, [FromQuery] int? size, CancellationToken ct)
    {
        var all = await roles.ListAsync(ct);
        var (skip, take) = (Paging.First(first), Paging.Size(size));

        return new RolesResponse(
            [.. all.Skip(skip).Take(take).Select(ToResponse)],
            skip,
            take,
            all.Count > skip + take ? skip + take : null);
    }

    [HttpGet("{roleId:guid}")]
    [ProducesResponseType<RoleDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RoleDetailResponse> GetById(Guid roleId, CancellationToken ct)
    {
        var (role, members) = await roles.GetAsync(roleId, ct);

        return new RoleDetailResponse(
            role.Id, role.Name, role.Description, role.Permissions, [.. members.Select(StaffController.ToSummary)]);
    }

    [HttpPost]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var role = await roles.CreateAsync(User.ToActor(), request.Name, request.Description, request.Permissions, ct);

        return CreatedAtAction(nameof(GetById), new { roleId = role.Id }, ToResponse(role));
    }

    /// <summary>Açıklamayı ve izinleri değiştirir. Sahip olduğun rolü değiştiremezsin.</summary>
    [HttpPut("{roleId:guid}")]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<RoleResponse> Update(Guid roleId, [FromBody] UpdateRoleRequest request, CancellationToken ct) =>
        ToResponse(await roles.UpdateAsync(User.ToActor(), roleId, request.Description, request.Permissions, ct));

    /// <summary>Rolü siler; kimde varsa onlardan da alınıyor. Sahip olduğun rolü silemezsin.</summary>
    [HttpDelete("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid roleId, CancellationToken ct)
    {
        await roles.DeleteAsync(User.ToActor(), roleId, ct);

        return NoContent();
    }

    private static RoleResponse ToResponse(DirectoryRole role) =>
        new(role.Id, role.Name, role.Description, role.Permissions);
}

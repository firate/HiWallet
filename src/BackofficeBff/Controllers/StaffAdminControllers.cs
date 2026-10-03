using System.Globalization;
using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BackofficeBff.Controllers;

// Personel yönetimi: staff-admin'e aynen iletiliyor. İzni ve "kendine yetki veremez"
// kurallarını iç servis kontrol ediyor; BFF reddi yorumlamadan aktarıyor.

/// <summary>
/// Oturumdaki çalışanın şu anki rolleri ve izinleri. Panel menüyü ve düğmeleri buna göre
/// gösteriyor; izni olmayan çalışana yalnızca rolünün olmadığını söylüyor. Personel
/// yönetimi izni istemiyor: her çalışan kendi iznini görüyor.
/// </summary>
[ApiController]
[Route("v1/me")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class MeController(StaffAdminClient staffAdmin) : ControllerBase
{
    [HttpGet]
    public Task<MeResponse> Get(CancellationToken ct) => staffAdmin.GetAsync<MeResponse>("v1/me", ct);
}

/// <summary>Kodun izinleri; rol tanımlarken bunlardan seçiliyor.</summary>
[ApiController]
[Route("v1/permissions")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class PermissionsController(StaffAdminClient staffAdmin) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PermissionResponse>> List(CancellationToken ct) =>
        staffAdmin.GetAsync<IReadOnlyList<PermissionResponse>>("v1/permissions", ct);
}

/// <summary>Panelin rolleri: izin setleri.</summary>
[ApiController]
[Route("v1/roles")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class RolesController(StaffAdminClient staffAdmin) : ControllerBase
{
    /// <param name="first">Önceki sayfanın <c>nextFirst</c> değeri.</param>
    [HttpGet]
    public Task<RolesResponse> List([FromQuery] int? first, [FromQuery] int? size, CancellationToken ct) =>
        staffAdmin.GetAsync<RolesResponse>(StaffAdminPaging.Query("v1/roles", first, size, search: null), ct);

    [HttpGet("{roleId:guid}")]
    [ProducesResponseType<RoleDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RoleDetailResponse> GetById(Guid roleId, CancellationToken ct) =>
        staffAdmin.GetAsync<RoleDetailResponse>($"v1/roles/{roleId}", ct);

    [HttpPost]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var role = await staffAdmin.PostAsync<RoleResponse>("v1/roles", request, idempotencyKey: null, ct);

        return CreatedAtAction(nameof(GetById), new { roleId = role.RoleId }, role);
    }

    [HttpPut("{roleId:guid}")]
    public Task<RoleResponse> Update(Guid roleId, [FromBody] UpdateRoleRequest request, CancellationToken ct) =>
        staffAdmin.PutAsync<RoleResponse>($"v1/roles/{roleId}", request, ct);

    [HttpDelete("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid roleId, CancellationToken ct)
    {
        await staffAdmin.DeleteAsync($"v1/roles/{roleId}", ct);

        return NoContent();
    }
}

/// <summary>Çalışanlar: davet, rol atama, kapatma.</summary>
[ApiController]
[Route("v1/staff")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class StaffController(StaffAdminClient staffAdmin) : ControllerBase
{
    /// <param name="first">Önceki sayfanın <c>nextFirst</c> değeri.</param>
    /// <param name="search">E-posta, ad ya da soyadında geçen metin.</param>
    [HttpGet]
    public Task<StaffPageResponse> List(
        [FromQuery] int? first, [FromQuery] int? size, [FromQuery] string? search, CancellationToken ct) =>
        staffAdmin.GetAsync<StaffPageResponse>(StaffAdminPaging.Query("v1/staff", first, size, search), ct);

    [HttpGet("{staffId:guid}")]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<StaffDetailResponse> GetById(Guid staffId, CancellationToken ct) =>
        staffAdmin.GetAsync<StaffDetailResponse>($"v1/staff/{staffId}", ct);

    /// <summary>Çalışanı açar, rollerini verir ve davet e-postasını gönderir.</summary>
    [HttpPost]
    [ProducesResponseType<StaffDetailResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Invite([FromBody] InviteStaffRequest request, CancellationToken ct)
    {
        var staff = await staffAdmin.PostAsync<StaffDetailResponse>("v1/staff", request, idempotencyKey: null, ct);

        return CreatedAtAction(nameof(GetById), new { staffId = staff.StaffId }, staff);
    }

    [HttpPut("{staffId:guid}/roles")]
    public Task<StaffDetailResponse> SetRoles(Guid staffId, [FromBody] SetStaffRolesRequest request, CancellationToken ct) =>
        staffAdmin.PutAsync<StaffDetailResponse>($"v1/staff/{staffId}/roles", request, ct);

    [HttpPost("{staffId:guid}/disable")]
    public Task<StaffDetailResponse> Disable(Guid staffId, CancellationToken ct) =>
        staffAdmin.PostAsync<StaffDetailResponse>($"v1/staff/{staffId}/disable", body: null, idempotencyKey: null, ct);

    [HttpPost("{staffId:guid}/enable")]
    public Task<StaffDetailResponse> Enable(Guid staffId, CancellationToken ct) =>
        staffAdmin.PostAsync<StaffDetailResponse>($"v1/staff/{staffId}/enable", body: null, idempotencyKey: null, ct);

    [HttpPost("{staffId:guid}/invitation")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendInvitation(Guid staffId, CancellationToken ct)
    {
        await staffAdmin.PostAsync($"v1/staff/{staffId}/invitation", ct);

        return Accepted();
    }
}

/// <summary>Personel yönetimindeki değişikliklerin kaydı, yeniden eskiye.</summary>
[ApiController]
[Route("v1/audit-events")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class AuditEventsController(StaffAdminClient staffAdmin) : ControllerBase
{
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri.</param>
    [HttpGet]
    public Task<AuditEventsResponse> List([FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct) =>
        staffAdmin.GetAsync<AuditEventsResponse>(
            InternalServiceClient.Paged("v1/audit-events", after?.ToString(), size), ct);
}

/// <summary>Sayfalama sıra numarasıyla; parametreler olduğu gibi iletiliyor.</summary>
internal static class StaffAdminPaging
{
    public static string Query(string path, int? first, int? size, string? search)
    {
        var query = new QueryBuilder();

        if (first is not null)
        {
            query.Add("first", first.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (size is not null)
        {
            query.Add("size", size.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("search", search);
        }

        return path + query;
    }
}

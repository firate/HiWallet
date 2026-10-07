using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.StaffAdmin.Api.Controllers;

/// <summary>
/// Token'ın sahibinin şu anki rolleri ve izinleri. İç servisler çalışanın her isteğinde
/// izni buradan soruyor, çalışanın kendi token'ıyla; başkasının izni sorulamıyor. Panel
/// menüyü ve düğmeleri buna göre gösteriyor.
///
/// Varsayılan politika: çalışanın token'ı yeter, izin istenmiyor. İzni olmaması da bir cevap.
/// </summary>
[ApiController]
[Route("v1/me")]
public sealed class MeController(StaffAccess access) : ControllerBase
{
    [HttpGet]
    public async Task<MeResponse> Get(CancellationToken ct)
    {
        var subject = User.Subject();
        var current = await access.OfAsync(subject, ct);

        return new MeResponse(subject, current.Roles, current.Permissions);
    }
}

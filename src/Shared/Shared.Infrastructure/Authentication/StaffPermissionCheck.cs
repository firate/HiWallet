using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.Shared.Infrastructure.Authentication;

/// <summary>
/// Çalışanın ŞU ANKİ izinleri. Token'da izin yok, yalnızca kimlik var: izinler personel
/// yönetiminin veritabanında ve her istekte oradan okunuyor. Rolü alınan ya da kapatılan
/// çalışanın bir sonraki isteği reddediliyor; önbellek yok.
/// </summary>
public interface IStaffPermissions
{
    /// <returns>Kapatılmış ya da panelden açılmamış çalışanda boş.</returns>
    /// <exception cref="StaffPermissionsUnavailableException">İzinler okunamadı; istek reddediliyor.</exception>
    Task<IReadOnlySet<string>> OfAsync(ClaimsPrincipal staff, CancellationToken ct);
}

/// <param name="Permission">Ucun istediği izin.</param>
/// <param name="CustomersPass">
/// Müşteri izinsiz geçiyor; sahipliği uç kendisi kontrol ediyor. Çalışan yine izin istiyor.
/// </param>
internal sealed record StaffPermissionRequirement(string Permission, bool CustomersPass = false)
    : IAuthorizationRequirement;

/// <summary>
/// Politikanın istediği izni çalışanın şu anki izinlerinde arıyor. Uçlar yalnızca
/// politikanın adını söylüyor (<see cref="HiWalletPolicies"/>); kontrol tek yerde.
/// İzinler bir istekte bir kez okunuyor.
/// </summary>
internal sealed class StaffPermissionHandler : AuthorizationHandler<StaffPermissionRequirement>
{
    private static readonly object RequestCacheKey = new();

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, StaffPermissionRequirement requirement)
    {
        if (!context.User.IsEmployee())
        {
            if (requirement.CustomersPass)
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (context.Resource is not HttpContext http)
        {
            return;
        }

        if ((await GrantedAsync(http, context.User)).Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }

    private static Task<IReadOnlySet<string>> GrantedAsync(HttpContext http, ClaimsPrincipal staff)
    {
        if (http.Items.TryGetValue(RequestCacheKey, out var cached) && cached is Task<IReadOnlySet<string>> granted)
        {
            return granted;
        }

        // Yalnızca çalışanı kabul eden serviste kayıtlı; müşterinin isteği buraya gelmiyor.
        granted = http.RequestServices.GetRequiredService<IStaffPermissions>().OfAsync(staff, http.RequestAborted);
        http.Items[RequestCacheKey] = granted;

        return granted;
    }
}

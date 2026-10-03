using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HiWallet.Shared.Infrastructure.Authentication;

/// <summary>
/// Çalışanın izinlerini personel yönetimine (<c>staff-admin</c>) soruyor:
/// <c>GET v1/me</c>, çalışanın KENDİ token'ıyla. Servis yalnızca token'ın sahibinin
/// izinlerini dönüyor; başkasınınkini soramıyor, ayrı bir servis kimliği de gerekmiyor.
///
/// Cevap alınamazsa istek reddediliyor (<c>503</c>): izni doğrulanamayan çalışan işlem
/// yapamıyor. Yeniden deneme yok; çalışan isteği tekrarlıyor.
/// </summary>
internal sealed class StaffAdminPermissions(HttpClient http, IHttpContextAccessor accessor) : IStaffPermissions
{
    public async Task<IReadOnlySet<string>> OfAsync(ClaimsPrincipal staff, CancellationToken ct)
    {
        // Token doğrulanmış kimliğin kaydından okunuyor, gelen başlıktan değil.
        var token = accessor.HttpContext is { } context
            ? await context.GetTokenAsync(AuthenticationSetup.StaffScheme, "access_token")
            : null;

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Çalışanın token'ı istekte yok; izinler sorulamaz.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new StaffPermissionsUnavailableException(
                    $"Personel yönetimi izinleri vermedi: {(int)response.StatusCode}.");
            }

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(ct)
                     ?? throw new StaffPermissionsUnavailableException("Personel yönetiminin cevabı boş.");

            return me.Permissions.ToHashSet(StringComparer.Ordinal);
        }
        catch (HttpRequestException exception)
        {
            throw new StaffPermissionsUnavailableException("Personel yönetimine ulaşılamadı.", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new StaffPermissionsUnavailableException("Personel yönetimi zamanında cevap vermedi.", exception);
        }
    }

    private sealed record MeResponse(IReadOnlyList<string> Permissions);
}

/// <summary>Çalışanın izinleri okunamadı → <c>503</c>.</summary>
public sealed class StaffPermissionsUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

internal sealed class StaffPermissionsExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<StaffPermissionsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not StaffPermissionsUnavailableException)
        {
            return false;
        }

        logger.LogWarning(exception, "Çalışanın izinleri okunamadı; istek reddedildi.");

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Yetki şu an doğrulanamıyor; tekrar dene."
            },
            Exception = exception
        });
    }
}

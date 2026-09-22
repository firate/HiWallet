using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.Shared.Infrastructure.Errors;

/// <summary>
/// Her servisin ortak hata yüzeyi (<c>baseline.md</c> madde 5): yakalanmamış her
/// istisna RFC 7807 gövdesiyle <c>500</c> döner.
///
/// <b>Gövde hiçbir iç detay taşımaz.</b> İstisna tipi, mesajı ve stack trace'i
/// yalnızca log'a gider; dışarıya çıkan tek bağ <c>traceId</c>. Çağıran o değeri
/// destek talebinde söylüyor, biz log'da aynı değeri arıyoruz.
///
/// <b>Domain bilgisi BURADA YOK.</b> Bu kütüphane wallet sınırını görmüyor
/// (<c>CLAUDE.md</c> "Deployable'lar"); iş kuralı reddini <c>422</c>'ye çeviren
/// handler'lar servisin kendi kurulumunda duruyor ve bu tabanın üstüne ekleniyor.
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddHiWalletProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Log ile response'u eşleştirebilmek için; OTel trace_id'siyle aynı değer.
                context.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString()
                    ?? context.HttpContext.TraceIdentifier;
            });

        return services;
    }
}

using System.Globalization;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.PersonalMobileApi.Responses;
using HiWallet.PersonalMobileApi.Setup;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

[ApiController]
[Route("v1/wallets")]
[EnableRateLimiting(RateLimitingSetup.CustomerPolicy)]
public sealed class WalletsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>Cüzdanın güncel bakiyesi, kova kırılımıyla.</summary>
    [HttpGet("{walletId:guid}")]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WalletResponse> GetById(Guid walletId, CancellationToken ct)
    {
        return await walletApi.GetAsync<WalletResponse>($"v1/wallets/{walletId}", ct);
    }

    /// <summary>Cüzdanın hareketleri, yeniden eskiye. Sayfalama cursor ile.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Verilmezse varsayılan; tavanın üstü tavana çekiliyor.</param>
    [HttpGet("{walletId:guid}/movements")]
    [ProducesResponseType<WalletMovementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WalletMovementsResponse> GetMovements(
        Guid walletId, [FromQuery] long? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await walletApi.GetAsync<WalletMovementsResponse>(
            Paged($"v1/wallets/{walletId}/movements", after?.ToString(CultureInfo.InvariantCulture), size), ct);
    }

    /// <summary>Cüzdanın promo partileri, yeniden eskiye. Sayfalama cursor ile.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Verilmezse varsayılan; tavanın üstü tavana çekiliyor.</param>
    [HttpGet("{walletId:guid}/promos")]
    [ProducesResponseType<WalletPromosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WalletPromosResponse> GetPromos(
        Guid walletId, [FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await walletApi.GetAsync<WalletPromosResponse>(
            Paged($"v1/wallets/{walletId}/promos", after?.ToString(), size), ct);
    }

    /// <summary>
    /// Verilmeyen parametre iletilmiyor: varsayılan sayfa boyutu ve tavan wallet-api'nin
    /// bilgisi, burada ikinci kopyası tutulmuyor.
    /// </summary>
    private static string Paged(string path, string? after, int? size)
    {
        var query = new QueryBuilder();

        if (after is not null)
        {
            query.Add("after", after);
        }

        if (size is not null)
        {
            query.Add("size", size.Value.ToString(CultureInfo.InvariantCulture));
        }

        return path + query;
    }
}

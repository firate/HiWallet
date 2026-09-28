using System.Globalization;
using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalWebBff.Controllers;

[ApiController]
[Route("v1/wallets")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
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
            InternalServiceClient.Paged(
                $"v1/wallets/{walletId}/movements", after?.ToString(CultureInfo.InvariantCulture), size), ct);
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
            InternalServiceClient.Paged($"v1/wallets/{walletId}/promos", after?.ToString(), size), ct);
    }
}

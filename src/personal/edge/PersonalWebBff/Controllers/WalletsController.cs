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
public sealed class WalletsController(WalletApiClient walletApi, WithdrawalOrchestratorClient orchestrator, CardTopupClient cardTopup) : ControllerBase
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

    /// <summary>
    /// Cüzdanın çekimleri, yeniden eskiye; orchestrator'dan aynen. Müşteri yalnızca kendi
    /// başlattığı çekimleri görüyor.
    /// </summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Verilmezse varsayılan; tavanın üstü tavana çekiliyor.</param>
    [HttpGet("{walletId:guid}/withdrawals")]
    [ProducesResponseType<WithdrawalsResponse>(StatusCodes.Status200OK)]
    public async Task<WithdrawalsResponse> GetWithdrawals(
        Guid walletId, [FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await orchestrator.GetAsync<WithdrawalsResponse>(
            InternalServiceClient.Paged($"v1/wallets/{walletId}/withdrawals", after?.ToString(), size), ct);
    }

    /// <summary>
    /// Cüzdanın kartla yüklemeleri, yeniden eskiye; card-topup'tan aynen. Müşteri yalnızca
    /// kendi başlattığı yüklemeleri görüyor.
    /// </summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Verilmezse varsayılan; tavanın üstü tavana çekiliyor.</param>
    [HttpGet("{walletId:guid}/card-topups")]
    [ProducesResponseType<CardTopupsResponse>(StatusCodes.Status200OK)]
    public async Task<CardTopupsResponse> GetCardTopups(
        Guid walletId, [FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await cardTopup.GetAsync<CardTopupsResponse>(
            InternalServiceClient.Paged($"v1/wallets/{walletId}/card-topups", after?.ToString(), size), ct);
    }
}

using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

[ApiController]
[Route("v1/accounts")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
/// <remarks>
/// Hesap buradan AÇILMIYOR: bireysel hesabı kayıt açıyor, kimlik doğrulandıktan sonra.
/// Buradan açılabilseydi kayıt ve doğrulama adımları atlanırdı.
/// </remarks>
public sealed class AccountsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>Kimliğin kullanıcısı olduğu hesaplar. Yeni bir cihazda hesap buradan bulunuyor.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Verilmezse varsayılan; tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<AccountsResponse>(StatusCodes.Status200OK)]
    public async Task<AccountsResponse> List([FromQuery] Guid? after, [FromQuery] int? size, CancellationToken ct)
    {
        return await walletApi.GetAsync<AccountsResponse>(
            InternalServiceClient.Paged("v1/accounts", after?.ToString(), size), ct);
    }

    /// <summary>Hesap ve altındaki cüzdanlar, bakiyeleriyle.</summary>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType<AccountDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AccountDetailResponse> GetById(Guid accountId, CancellationToken ct)
    {
        return await walletApi.GetAsync<AccountDetailResponse>($"v1/accounts/{accountId}", ct);
    }

    /// <summary>Hesaba cüzdan açar. Bakiye sıfırla başlıyor.</summary>
    [HttpPost("{accountId:guid}/wallets")]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> OpenWallet(
        Guid accountId, [FromBody] OpenWalletRequest request, CancellationToken ct)
    {
        var response = await walletApi.PostAsync<WalletResponse>(
            $"v1/accounts/{accountId}/wallets", request, idempotencyKey: null, ct);

        return CreatedAtAction(
            actionName: nameof(WalletsController.GetById),
            controllerName: "Wallets",
            routeValues: new { walletId = response.WalletId },
            value: response);
    }

    /// <summary>
    /// Bu para biriminin varsayılan cüzdanını değiştirir: hesap numarasına gelen para
    /// bundan sonra oraya.
    /// </summary>
    [HttpPut("{accountId:guid}/default-wallets/{currency}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetDefaultWallet(
        Guid accountId, string currency, [FromBody] SetDefaultWalletRequest request, CancellationToken ct)
    {
        await walletApi.PutAsync(
            $"v1/accounts/{accountId}/default-wallets/{Uri.EscapeDataString(currency)}", request, ct);

        return NoContent();
    }
}

using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.PersonalMobileApi.Controllers;

[ApiController]
[Route("v1/accounts")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class AccountsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>
    /// Bireysel hesap açar; token'daki kimlik hesabın kullanıcısı oluyor. Hesap tipi
    /// istemciden ALINMIYOR: bu ön API yalnızca bireysel müşteriye hizmet ediyor.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Open(CancellationToken ct)
    {
        var response = await walletApi.PostAsync<AccountResponse>(
            "v1/accounts", new { type = "Person" }, idempotencyKey: null, ct);

        return CreatedAtAction(nameof(GetById), new { accountId = response.AccountId }, response);
    }

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
}

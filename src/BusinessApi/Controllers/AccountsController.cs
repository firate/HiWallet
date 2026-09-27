using HiWallet.EdgeApi.Contracts;
using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HiWallet.BusinessApi.Controllers;

/// <summary>
/// İşyeri hesabı bu ön API'den AÇILMIYOR: işyerinin kaydı ve entegrasyonunun hesaba
/// bağlanması bir kayıt süreci, entegrasyonun kendi kendine yapacağı bir iş değil.
/// </summary>
[ApiController]
[Route("v1/accounts")]
[EnableRateLimiting(EdgeRateLimiting.ClientPolicy)]
public sealed class AccountsController(WalletApiClient walletApi) : ControllerBase
{
    /// <summary>Entegrasyonun bağlı olduğu hesaplar.</summary>
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
}

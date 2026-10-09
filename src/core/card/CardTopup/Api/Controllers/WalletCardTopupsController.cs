using HiWallet.CardTopup.Api.Responses;
using HiWallet.CardTopup.Application;
using HiWallet.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.CardTopup.Api.Controllers;

[ApiController]
[Route("v1/wallets/{walletId:guid}/card-topups")]
public sealed class WalletCardTopupsController(CardTopupQueries queries) : ControllerBase
{
    /// <summary>
    /// Cüzdanın kartla yüklemeleri, yeniden eskiye. Servis hesabın kullanıcılarını bilmiyor:
    /// müşteri yalnızca kendi başlattığı yüklemeleri görüyor, başkasının cüzdanında liste boş.
    /// Çalışan <c>customer.view</c> izniyle cüzdanın bütün yüklemelerini görüyor.
    /// </summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<CardTopupsResponse>(StatusCodes.Status200OK)]
    public async Task<CardTopupsResponse> List(
        Guid walletId,
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = CardTopupPage.DefaultSize)
    {
        var subject = User.IsEmployee() ? null : User.Subject();

        return CardTopupsResponse.From(await queries.ListForWalletAsync(walletId, subject, after, size, ct));
    }
}

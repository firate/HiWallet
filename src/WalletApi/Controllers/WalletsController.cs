using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.Balances;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletApi.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/wallets")]
public sealed class WalletsController(IMessageBus bus, AccountAccess access) : ControllerBase
{
    /// <summary>
    /// Cüzdanın güncel bakiyesi. Sistem hesapları (clearing, revenue) bu endpoint'ten
    /// GÖRÜNMEZ — onlar iç muhasebe, public API'nin cevaplayacağı soru değil.
    /// </summary>
    [HttpGet("{walletId:guid}")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletResponse>> GetById(Guid walletId, CancellationToken ct)
    {
        await access.EnsureViewableWalletAsync(User, walletId, ct);

        var view = await bus.InvokeAsync<WalletView>(new GetWalletQuery(walletId), ct);

        return Ok(WalletResponse.From(view));
    }

    /// <summary>
    /// Cüzdanın hareketleri, yeniden eskiye. Sayfalama CURSOR ile
    /// (<c>baseline.md</c> madde 8): ledger append-only ve yeni satırlar listenin
    /// başına giriyor, offset kullanılsaydı iki sayfa arasında gelen bir hareket
    /// sayfayı kaydırır ve müşteri aynı kaydı iki kez görürdü.
    /// </summary>
    /// <param name="after">
    /// Önceki sayfanın son hareketinin kimliği. İlk sayfada verilmiyor.
    /// </param>
    /// <param name="size">
    /// Sayfa boyutu. Tavanın üstü reddedilmiyor, tavana çekiliyor — istemciyi
    /// kırmadan sunucuyu koruyor.
    /// </param>
    [HttpGet("{walletId:guid}/movements")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<WalletMovementsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletMovementsResponse>> GetMovements(
        Guid walletId,
        CancellationToken ct,
        [FromQuery] long? after = null,
        [FromQuery] int size = WalletMovementPage.DefaultSize)
    {
        await access.EnsureViewableWalletAsync(User, walletId, ct);

        var page = await bus.InvokeAsync<WalletMovementPage>(
            new GetWalletMovementsQuery(walletId, after, size), ct);

        return Ok(WalletMovementsResponse.From(page));
    }

    /// <summary>
    /// Cüzdanın promo partileri, yeniden eskiye (decisions.md madde 37). Toplam promo
    /// bakiyesi "bu işyerinde ne kadar kullanabilirim" sorusunu cevaplamıyor; her
    /// partinin kalanı, bitişi ve geçerli olduğu işyerleri burada.
    /// </summary>
    /// <param name="after">Önceki sayfanın son partisinin kimliği. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet("{walletId:guid}/promos")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<WalletPromosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletPromosResponse>> GetPromos(
        Guid walletId,
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = WalletPromoPage.DefaultSize)
    {
        await access.EnsureViewableWalletAsync(User, walletId, ct);

        var page = await bus.InvokeAsync<WalletPromoPage>(new GetWalletPromosQuery(walletId, after, size), ct);

        return Ok(WalletPromosResponse.From(page));
    }

    /// <summary>
    /// Personel promo'su: çalışanın müşteriye platform fonlu promo vermesi (decisions.md
    /// madde 37). Pazarlama rolü; tutar para birimi başına tek seferlik tavanla sınırlı.
    /// Ledger'da aktör çalışan.
    /// </summary>
    /// <param name="idempotencyKey">
    /// ZORUNLU: para hareket ettiriyor. Kapsamı promo'yu alan cüzdan; aynı anahtarla
    /// ikinci istek yeni parti açmaz, mevcut partiyi <c>replayed: true</c> ile döner.
    /// </param>
    [HttpPost("{walletId:guid}/promos")]
    [Authorize(Policy = HiWalletPolicies.Marketing)]
    [ProducesResponseType<PromoGrantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PromoGrantResponse>> GrantStaffPromo(
        Guid walletId,
        [FromBody] GrantStaffPromoRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["Idempotency-Key"] = ["Idempotency-Key başlığı zorunlu."]
                }));
        }

        var result = await bus.InvokeAsync<GrantPromoResult>(
            request.ToCommand(walletId, User.Subject(), idempotencyKey), ct);

        return CreatedAtAction(
            actionName: nameof(GetPromos),
            routeValues: new { walletId },
            value: PromoGrantResponse.From(result));
    }
}

using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletApi.Setup;
using HiWallet.WalletService.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace HiWallet.WalletApi.Controllers;

[ApiController]
[Route("v1/accounts")]
public sealed class AccountsController(IMessageBus bus, AccountAccess access) : ControllerBase
{
    /// <summary>
    /// İşyeri hesabı açar. Para tutmaz; para cüzdanlarda durur ve bir hesabın aynı
    /// para biriminde birden fazla cüzdanı olabilir (decisions.md madde 20). Hesabı
    /// açan kimlik hesabın kullanıcısı oluyor. Bireysel hesabı kayıt açıyor
    /// (<c>POST /v1/person-accounts</c>).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountResponse>> Open(
        [FromBody] OpenAccountRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<OpenAccountResult>(request.ToCommand(User.Subject()), ct);
        var response = AccountResponse.From(result);

        return CreatedAtAction(
            actionName: nameof(GetById),
            routeValues: new { accountId = response.AccountId },
            value: response);
    }

    /// <summary>Kimliğin kullanıcısı olduğu hesaplar, yeniden eskiye.</summary>
    /// <param name="after">Önceki sayfanın <c>nextCursor</c> değeri. İlk sayfada verilmiyor.</param>
    /// <param name="size">Sayfa boyutu. Tavanın üstü tavana çekiliyor.</param>
    [HttpGet]
    [ProducesResponseType<AccountsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountsResponse>> List(
        CancellationToken ct,
        [FromQuery] Guid? after = null,
        [FromQuery] int size = AccountPage.DefaultSize)
    {
        var page = await bus.InvokeAsync<AccountPage>(new ListAccountsQuery(User.Subject(), after, size), ct);

        return Ok(AccountsResponse.From(page));
    }

    /// <summary>Hesap ve altındaki cüzdanlar, bakiyeleriyle.</summary>
    [HttpGet("{accountId:guid}")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<AccountDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDetailResponse>> GetById(Guid accountId, CancellationToken ct)
    {
        await access.EnsureViewableAccountAsync(User, accountId, ct);

        var view = await bus.InvokeAsync<AccountView>(new GetAccountQuery(accountId), ct);

        return Ok(AccountDetailResponse.From(view));
    }

    /// <summary>
    /// Hesaba cüzdan açar. Bakiye satırı aynı transaction'da açılır — cüzdan var ama
    /// bakiye satırı yok diye bir ara durum oluşmaz.
    /// </summary>
    [HttpPost("{accountId:guid}/wallets")]
    [ProducesResponseType<WalletResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WalletResponse>> OpenWallet(
        Guid accountId,
        [FromBody] OpenWalletRequest request,
        CancellationToken ct)
    {
        await access.EnsureAccountAsync(User.Subject(), accountId, ct);

        var result = await bus.InvokeAsync<OpenWalletResult>(request.ToCommand(accountId), ct);
        var response = WalletResponse.From(result);

        return CreatedAtAction(
            actionName: nameof(WalletsController.GetById),
            controllerName: "Wallets",
            routeValues: new { walletId = response.WalletId },
            value: response);
    }

    /// <summary>
    /// İşyerinin platform fonlu promo kabulü (decisions.md madde 37). <c>merchant.promo_acceptance</c> izni.
    /// Bundan sonraki ödemeleri etkiliyor; verilmiş partiler olduğu gibi kalıyor.
    /// </summary>
    [HttpPut("{accountId:guid}/accepts-promo")]
    [Authorize(Policy = HiWalletPolicies.MerchantPromoAcceptance)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetAcceptsPromo(
        Guid accountId, [FromBody] SetAcceptsPromoRequest request, CancellationToken ct)
    {
        await bus.InvokeAsync(new SetAcceptsPromoCommand(accountId, request.AcceptsPromo), ct);

        return NoContent();
    }

    /// <summary>
    /// Bireysel hesabın doğrulama seviyesini yükseltir. Yalnızca onboarding çağırıyor.
    /// Hesap zaten o seviyede ya da üstündeyse değişmiyor ve mevcut seviye dönüyor:
    /// seviyeyi yükselten yollar birbirinden habersiz, geç gelen bir alt seviye ulaşılmış
    /// üst seviyeyi geri almamalı.
    /// </summary>
    [HttpPut("{accountId:guid}/kyc-level")]
    [Authorize(Policy = OnboardingAccess.Policy)]
    [ProducesResponseType<KycLevelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KycLevelResponse>> RaiseKycLevel(
        Guid accountId,
        [FromBody] RaiseKycLevelRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<KycLevelResult>(request.ToCommand(accountId), ct);

        return Ok(KycLevelResponse.From(result));
    }
}

using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletApi.Responses;
using HiWallet.WalletApi.Setup;
using HiWallet.WalletApi.Validators;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
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
    /// Hesap numarasıyla kayıt: panelde arama ve müşteriyle konuşma. Görüntüleme kuralı
    /// kimlikle açılanın aynısı. Başkasının numarası da <c>404</c>: numaranın bir hesaba
    /// ait olduğu ve o hesabın kimliği dışarı verilmiyor.
    /// </summary>
    /// <param name="number">On hane; gruplama boşlukları kabul ediliyor. Kontrol hanesi tutmazsa <c>400</c>.</param>
    [HttpGet("by-number/{number}")]
    [Authorize(Policy = HiWalletPolicies.CustomerOrStaff)]
    [ProducesResponseType<AccountDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDetailResponse>> GetByNumber(string number, CancellationToken ct)
    {
        if (!AccountNumber.TryFrom(number, out var parsed))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(number)] = ["Hesap numarası geçersiz: on hane ve son hanesi kontrol hanesi."]
            }));
        }

        var accountId = await bus.InvokeAsync<Guid>(new FindAccountByNumberQuery(parsed), ct);

        try
        {
            await access.EnsureViewableAccountAsync(User, accountId, ct);
        }
        catch (AccountNotFoundException)
        {
            throw new AccountNumberNotFoundException(parsed);
        }

        var view = await bus.InvokeAsync<AccountView>(new GetAccountQuery(accountId), ct);

        return Ok(AccountDetailResponse.From(view));
    }

    /// <summary>
    /// Bu para biriminin varsayılan cüzdanını değiştirir: hesap numarasına gelen para
    /// bundan sonra oraya. Müşterinin tercihi; çalışan değiştirmiyor.
    /// </summary>
    /// <param name="currency">ISO 4217 kodu; cüzdanın para birimi bu olmalı.</param>
    [HttpPut("{accountId:guid}/default-wallets/{currency}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetDefaultWallet(
        Guid accountId, string currency, [FromBody] SetDefaultWalletRequest request, CancellationToken ct)
    {
        if (!CurrencyRules.IsValid(currency))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(currency)] = ["Para birimi 3 büyük harften oluşan ISO 4217 kodu olmalı."]
            }));
        }

        await access.EnsureAccountAsync(User.Subject(), accountId, ct);
        await bus.InvokeAsync(new SetDefaultWalletCommand(accountId, currency, request.WalletId), ct);

        return NoContent();
    }

    /// <summary>
    /// Hesaba cüzdan açar. Bakiye satırı aynı transaction'da açılır — cüzdan var ama
    /// bakiye satırı yok diye bir ara durum oluşmaz. Hesabın bu para birimindeki ilk
    /// cüzdanı varsayılan oluyor.
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

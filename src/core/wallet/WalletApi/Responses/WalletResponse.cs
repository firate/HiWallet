using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.Balances;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletApi.Responses;

/// <param name="Balance">
/// <c>ledger_balances</c> projeksiyonundan, kovaların toplamı. Açılışta her zaman
/// sıfır — cüzdana para yalnızca ledger üzerinden girer (top-up ya da transfer),
/// doğrudan yazılamaz.
/// </param>
/// <param name="Withdrawable">
/// IBAN'a çıkabilen kısım (<c>decisions.md</c> madde 36). Toplamdan AYRI dönüyor:
/// kart ile yüklenen ve hediye bakiye nakde çevrilemiyor, tek bir toplam dönseydi
/// müşteri çekimin neden reddedildiğini göremezdi.
/// </param>
/// <param name="Balances">
/// Kova kırılımı. Sıfır bakiyeli kovalar da dönüyor: hangi kovaların var olduğunu
/// göstermek, olmayan bir kovanın sessizce kaybolmasından iyi.
/// </param>
public sealed record WalletResponse(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances)
{
    public static WalletResponse From(OpenWalletResult result)
    {
        // Yeni cüzdanın her kovası sıfır. Boş liste dönmek yerine kovalar açıkça
        // yazılıyor ki response'un şekli açılışta da sorguda da aynı olsun.
        return new WalletResponse(
            result.WalletId, result.AccountId, result.Name, result.Currency,
            result.Balance, result.Balance, Empty());
    }

    public static WalletResponse From(WalletView view)
    {
        return new WalletResponse(
            view.WalletId, view.AccountId, view.Name, view.Currency,
            view.Balance, view.Withdrawable,
            view.Balances.Select(b => new WalletBalanceResponse(b.FundType, b.Balance)).ToArray());
    }

    private static WalletBalanceResponse[] Empty() =>
        [.. FundTypes.All.Select(fundType => new WalletBalanceResponse(fundType.ToText(), 0m))];
}

public sealed record WalletBalanceResponse(string FundType, decimal Balance);

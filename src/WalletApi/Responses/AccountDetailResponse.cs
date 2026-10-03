using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

/// <param name="AccountNumber">İnsanın kullandığı on haneli numara; hesaba gelen para bu numarayla.</param>
/// <param name="KycLevel">Bireysel hesabın doğrulama seviyesi; işyeri hesabında <c>null</c>.</param>
/// <param name="AcceptsPromo">Platform fonlu promo bu işyerinde geçiyor mu. Bireysel hesapta hep <c>false</c>.</param>
public sealed record AccountDetailResponse(
    Guid AccountId,
    string AccountNumber,
    AccountType Type,
    KycLevel? KycLevel,
    bool AcceptsPromo,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletResponse> Wallets)
{
    public static AccountDetailResponse From(AccountView view)
    {
        return new AccountDetailResponse(
            view.AccountId,
            view.Number.Value,
            view.Type,
            view.KycLevel,
            view.AcceptsPromo,
            view.CreatedAt,
            view.Wallets
                .Select(w => new AccountWalletResponse(
                    w.WalletId, w.Name, w.Currency, w.Balance, w.Withdrawable,
                    [.. w.Balances.Select(b => new WalletBalanceResponse(b.FundType, b.Balance))],
                    w.IsDefault))
                .ToArray());
    }
}

/// <summary>
/// Liste görünümü de kırılımı taşıyor: hesap ekranında "neden çekemiyorum"
/// sorusunun cevabı tek cüzdana girmeden görünsün (decisions.md madde 36).
/// </summary>
/// <param name="IsDefault">Para biriminin varsayılan cüzdanı: hesap numarasına gelen para buraya.</param>
public sealed record AccountWalletResponse(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceResponse> Balances,
    bool IsDefault);

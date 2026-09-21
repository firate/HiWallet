using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Hesap ve altındaki cüzdanlar. Cüzdan listesi buraya bağlı: bir hesabın aynı para
/// biriminde birden fazla cüzdanı olabildiği için (decisions.md madde 20) "hesabın
/// cüzdanı" tekil bir şey değil, sorulması gereken bir liste.
/// </summary>
public sealed record GetAccountQuery(Guid AccountId);

public sealed record AccountView(
    Guid AccountId,
    AccountType Type,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountWalletView> Wallets);

/// <param name="Balance">Kovaların toplamı: müşterinin gördüğü bakiye.</param>
/// <param name="Withdrawable">
/// IBAN'a çıkabilen kısım (decisions.md madde 36). Toplamdan ayrı dönüyor çünkü
/// kart ile yüklenen ve hediye bakiye nakde çevrilemiyor; tek bir toplam
/// dönseydi müşteri çekim reddedildiğinde sebebini göremezdi.
/// </param>
/// <param name="Balances">
/// Kova kırılımı. Sıfır bakiyeli kovalar da dönüyor: müşteriye hangi kovaların
/// var olduğunu göstermek, olmayan bir kovanın sessizce kaybolmasından iyi.
/// </param>
public sealed record AccountWalletView(
    Guid WalletId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletBalanceView> Balances);

public sealed record WalletBalanceView(string FundType, decimal Balance);

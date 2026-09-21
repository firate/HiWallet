namespace HiWallet.WalletService.Application.Balances;

/// <summary>
/// Cüzdanın güncel durumu. Bakiye <c>ledger_balances</c>'tan okunuyor,
/// <c>ledger_entries</c> toplanarak DEĞİL: projeksiyon tam da bunun için var
/// (docs/ledger-schema.md "ledger_balances").
/// </summary>
public sealed record GetWalletQuery(Guid WalletId);

/// <param name="Balance">Kovaların toplamı: müşterinin gördüğü bakiye.</param>
/// <param name="Withdrawable">
/// IBAN'a çıkabilen kısım (decisions.md madde 36). Ayrı dönüyor çünkü kart ile
/// yüklenen ve hediye bakiye nakde çevrilemiyor; tek toplam dönseydi müşteri
/// çekim reddedildiğinde sebebini göremezdi.
/// </param>
/// <param name="Balances">Kova kırılımı, sıfır bakiyeli kovalar dahil.</param>
/// <param name="UpdatedAt">Kovaların en son güncelleneni.</param>
public sealed record WalletView(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance,
    decimal Withdrawable,
    IReadOnlyList<WalletFundBalanceView> Balances,
    DateTimeOffset UpdatedAt);

public sealed record WalletFundBalanceView(string FundType, decimal Balance);

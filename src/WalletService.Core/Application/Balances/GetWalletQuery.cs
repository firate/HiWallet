namespace HiWallet.WalletService.Application.Balances;

/// <summary>
/// Cüzdanın güncel durumu. Bakiye <c>ledger_balances</c>'tan okunuyor,
/// <c>ledger_entries</c> toplanarak DEĞİL: projeksiyon tam da bunun için var
/// (docs/ledger-schema.md "ledger_balances").
/// </summary>
public sealed record GetWalletQuery(Guid WalletId);

public sealed record WalletView(
    Guid WalletId,
    Guid AccountId,
    string Name,
    string Currency,
    decimal Balance,
    DateTimeOffset UpdatedAt);

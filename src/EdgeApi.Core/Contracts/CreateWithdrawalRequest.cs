namespace HiWallet.EdgeApi.Contracts;

/// <summary>
/// Hesap kimliği istemciden ALINMIYOR: cüzdanın hesabı wallet-api'den okunuyor.
/// </summary>
/// <param name="Amount">Müşteriye ulaşacak tutar. Komisyon buna EK olarak düşülür.</param>
/// <param name="DestinationIban">Boşluklu yazılabilir. Doğrulamayı orchestrator yapıyor.</param>
public sealed record CreateWithdrawalRequest(
    Guid WalletId,
    decimal Amount,
    string Currency,
    string DestinationIban);

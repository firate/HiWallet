namespace HiWallet.PersonalMobileApi.Requests;

/// <param name="Amount">Alıcıya geçecek tutar. Komisyon buna EK olarak gönderenden düşülür.</param>
/// <param name="Type"><c>P2P</c>, <c>P2B</c> ya da <c>Payment</c>. Değeri wallet-api doğruluyor.</param>
public sealed record CreateTransferRequest(
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    string Currency,
    string Type);

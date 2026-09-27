namespace HiWallet.EdgeApi.Contracts;

/// <param name="Amount">Alıcıya geçecek tutar. Komisyon buna EK olarak gönderenden düşülür.</param>
/// <param name="Type">
/// <c>P2P</c>, <c>P2B</c>, <c>B2P</c>, <c>B2B</c> ya da <c>Payment</c>. Değeri ve hesap
/// tipleriyle uyumunu wallet-api doğruluyor.
/// </param>
public sealed record CreateTransferRequest(
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    string Currency,
    string Type);

using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletApi.Requests;

/// <param name="Amount">
/// Alıcıya geçecek tutar. Komisyon buna EK olarak gönderenden düşülür.
/// </param>
public sealed record CreateTransferRequest(
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    string Currency,
    TransferType Type)
{
    /// <summary>
    /// <c>Idempotency-Key</c> header'ından gelir, gövdeden değil — HTTP semantiği bu
    /// ve client'ın aynı gövdeyi tekrar göndermesi yeterli olmalı.
    /// </summary>
    public CreateTransferCommand ToCommand(string? idempotencyKey)
    {
        return new CreateTransferCommand(
            FromWalletId, ToWalletId, Amount, Currency, Type, idempotencyKey);
    }
}

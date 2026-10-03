using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletApi.Requests;

/// <param name="ToWalletId">Alan cüzdan. Alıcı ya bununla ya <paramref name="ToAccountNumber"/> ile.</param>
/// <param name="ToAccountNumber">
/// Alıcının hesap numarası; para alıcının bu para birimindeki varsayılan cüzdanına düşüyor.
/// </param>
/// <param name="Amount">
/// Alıcıya geçecek tutar. Komisyon buna EK olarak gönderenden düşülür.
/// </param>
public sealed record CreateTransferRequest(
    Guid FromWalletId,
    Guid? ToWalletId,
    string? ToAccountNumber,
    decimal Amount,
    string Currency,
    TransferType Type)
{
    /// <summary>
    /// <c>Idempotency-Key</c> header'ından gelir, gövdeden değil — HTTP semantiği bu
    /// ve client'ın aynı gövdeyi tekrar göndermesi yeterli olmalı.
    /// </summary>
    /// <param name="toWalletId">Alan cüzdan: verilen ya da hesap numarasından çözülen.</param>
    public CreateTransferCommand ToCommand(string idempotencyKey, Guid toWalletId)
    {
        return new CreateTransferCommand(
            FromWalletId, toWalletId, Amount, Currency, Type, idempotencyKey);
    }
}

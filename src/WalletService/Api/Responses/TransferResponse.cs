using HiWallet.WalletService.Application.Transfers;

namespace HiWallet.WalletService.Api.Responses;

/// <param name="Replayed">
/// <c>true</c> ise bu <c>Idempotency-Key</c> daha önce işlenmişti; yeni bir transfer
/// yapılmadı, mevcut işlemin kimliği dönüyor.
/// </param>
public sealed record TransferResponse(Guid TransactionId, bool Replayed)
{
    public static TransferResponse From(TransferResult result)
    {
        return new TransferResponse(result.TransactionId, result.Replayed);
    }
}

namespace HiWallet.PersonalMobileApi.Responses;

/// <param name="Replayed">
/// <c>true</c> ise bu <c>Idempotency-Key</c> daha önce işlenmişti; yeni bir transfer
/// yapılmadı, mevcut işlemin kimliği dönüyor.
/// </param>
public sealed record TransferResponse(Guid TransactionId, bool Replayed);

namespace HiWallet.WalletService.Application.Topups;

/// <param name="LedgerTransactionId">
/// Ledger'a yazılan işlem. Tekrar gelen mesajda ORİJİNAL işlemin kimliği dönüyor.
/// </param>
/// <param name="Replayed">
/// Bu mesaj daha önce işlenmişti; ledger'a dokunulmadı.
/// </param>
public readonly record struct ProcessTopupResult(Guid? LedgerTransactionId, bool Replayed);

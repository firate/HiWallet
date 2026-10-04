using HiWallet.WalletService.Domain.Deposits;

namespace HiWallet.WalletService.Application.Deposits;

/// <param name="LedgerTransactionId">
/// Ledger'a yazılan işlem: cüzdana yükleme ya da askı. Tekrar gelen mesajda ORİJİNAL
/// işlemin kimliği dönüyor.
/// </param>
/// <param name="Replayed">Bu havale daha önce işlenmişti; ledger'a dokunulmadı.</param>
/// <param name="HeldFor">
/// Askıya alındıysa sebebi, cüzdana yazıldıysa <c>null</c>. Tekrar gelen mesajda
/// bilinmiyor ve <c>null</c>: karar ilk işlemede verildi.
/// </param>
public readonly record struct ProcessDepositResult(Guid LedgerTransactionId, bool Replayed, DepositHoldReason? HeldFor);

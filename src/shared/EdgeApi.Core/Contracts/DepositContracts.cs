namespace HiWallet.EdgeApi.Contracts;

/// <summary>
/// Havaleyle yükleme bilgisi: bu IBAN'a, alıcı adı bu, açıklamaya bu numara. Havale yalnızca
/// müşterinin kendi adına kayıtlı hesabından kabul ediliyor.
/// </summary>
/// <param name="Reference">Hesap numarası: açıklamaya yazılacak.</param>
public sealed record DepositInstructionsResponse(string Iban, string AccountHolder, string Reference, string Currency);

/// <summary>Cüzdana geçirilemeyip askıya alınan havale.</summary>
/// <param name="Reason">
/// <c>no_account_number</c>, <c>ambiguous_account_number</c>, <c>unknown_account</c>,
/// <c>business_account</c>, <c>no_wallet_in_currency</c>, <c>unknown_sender</c>,
/// <c>sender_not_holder</c>, <c>limit_exceeded</c>.
/// </param>
/// <param name="Status"><c>open</c>: karara açık; <c>returning</c>: iadesi sürüyor.</param>
public sealed record SuspendedDepositResponse(
    Guid Id,
    string Provider,
    string BankReference,
    decimal Amount,
    string Currency,
    string Reason,
    Guid? AccountId,
    string? AccountNumber,
    DateTimeOffset ReceivedAt,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record SuspendedDepositsResponse(IReadOnlyList<SuspendedDepositResponse> Items, int Size, Guid? NextCursor);

/// <param name="AccountNumber">Paranın aktarılacağı hesap; para varsayılan cüzdanına düşüyor.</param>
public sealed record MoveSuspendedDepositRequest(string AccountNumber);

/// <param name="Replayed">Aynı anahtarla tekrar: ikinci kez yazılmadı.</param>
public sealed record SuspendedDepositMovedResponse(
    Guid SuspendedDepositId,
    Guid AccountId,
    Guid WalletId,
    Guid LedgerTransactionId,
    bool Replayed);

namespace HiWallet.Bank.Fake.Api.Responses;

/// <summary>
/// <c>POST /v1/transfers</c> yanıtı. Verilen söz "gönderdim" DEĞİL, "aldım":
/// <c>status</c> kabul anında <c>pending</c>.
/// </summary>
/// <param name="Replayed">
/// Aynı idempotency anahtarıyla ikinci istek. Transfer TEKRARLANMADI, mevcut kayıt
/// dönüldü. Bankanın kendi koruması — bizim tarafımızdaki dedup'tan bağımsız.
/// </param>
public sealed record StartTransferResponse(string BankReference, string Status, bool Replayed);

/// <summary>
/// <c>GET /v1/transfers/{bankReference}</c> yanıtı. Mutabakat taramasının okuduğu şey.
/// </summary>
public sealed record TransferStatusResponse(
    string BankReference,
    string ClientReference,
    string Status,
    decimal Amount,
    decimal Fee,
    string Currency,
    string? FailureReason,
    DateTimeOffset AcceptedAt);

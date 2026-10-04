namespace HiWallet.Bank.Fake.Api.Responses;

/// <summary><c>POST /v1/incoming-transfers</c> response'u: bankanın havaleye verdiği referans.</summary>
public sealed record IncomingTransferAcceptedResponse(string BankReference);

/// <summary><c>GET /v1/incoming-transfers</c>: hesap hareketleri, geliş sırasıyla.</summary>
public sealed record IncomingTransfersResponse(IReadOnlyList<IncomingTransferItem> Items);

public sealed record IncomingTransferItem(
    string BankReference,
    decimal Amount,
    string Currency,
    string? Description,
    string? SenderName,
    string? SenderIban,
    string? SenderNationalId,
    DateTimeOffset ReceivedAt);

using HiWallet.WalletService.Application.Balances;

namespace HiWallet.WalletApi.Responses;

/// <param name="MovementId">
/// Hareketin kimliği ve aynı zamanda cursor. İstemci bir sonraki sayfayı
/// <c>?after=</c> ile bu değeri göndererek alıyor.
/// </param>
/// <param name="Amount">İşaret yön taşır: cüzdana giren <c>+</c>, çıkan <c>-</c>.</param>
public sealed record WalletMovementResponse(
    long MovementId,
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    string FundType,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor">
/// Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>: istemci
/// listenin bittiğini buradan anlıyor, toplam sayı sorgusu koşulmuyor.
/// </param>
public sealed record WalletMovementsResponse(
    IReadOnlyList<WalletMovementResponse> Items,
    int Size,
    long? NextCursor)
{
    public static WalletMovementsResponse From(WalletMovementPage page)
    {
        return new WalletMovementsResponse(
            [.. page.Items.Select(m => new WalletMovementResponse(
                m.MovementId, m.TransactionId, m.Type, m.Amount,
                m.Currency, m.FundType, m.CreatedAt))],
            page.Size,
            page.NextCursor);
    }
}

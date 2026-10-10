using HiWallet.CardTopup.Application;
using HiWallet.CardTopup.Domain;

namespace HiWallet.CardTopup.Api.Responses;

/// <summary>Kartla yükleme ve son durumu.</summary>
/// <param name="State"><c>created</c>, <c>pending</c>, <c>paid</c>, <c>failed</c>, <c>rejected</c>.</param>
/// <param name="PaymentUrl">
/// Müşterinin kartını gireceği sayfa. Ödeme açıkken dolu; ödeme açılmadıysa ya da kapandıysa
/// <c>null</c>.
/// </param>
/// <param name="FailureReason">Ödenmeden kapandıysa ya da reddedildiyse sebebi.</param>
public sealed record CardTopupResponse(
    Guid CardTopupId,
    Guid WalletId,
    string State,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    DateTimeOffset ExpiresAt,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CardTopupResponse From(Domain.CardTopup topup) =>
        new(
            topup.Id,
            topup.WalletId,
            topup.State.ToText(),
            topup.Amount,
            topup.Currency,
            topup.PaymentUrl,
            topup.ExpiresAt,
            topup.FailureReason,
            topup.CreatedAt,
            topup.UpdatedAt);
}

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record CardTopupsResponse(IReadOnlyList<CardTopupResponse> Items, int Size, Guid? NextCursor)
{
    public static CardTopupsResponse From(CardTopupPage page) =>
        new([.. page.Items.Select(CardTopupResponse.From)], page.Size, page.NextCursor);
}

/// <summary>
/// <c>POST /v1/card-topups</c> response'u. <c>202</c>: dönüldüğünde hiçbir para hareket
/// etmedi, müşteri ödeme sayfasına gidecek.
/// </summary>
/// <param name="Replayed">Bu anahtarla yükleme zaten başlatılmıştı; yenisi açılmadı.</param>
public sealed record CardTopupAcceptedResponse(
    Guid CardTopupId,
    Guid WalletId,
    string State,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    DateTimeOffset ExpiresAt,
    string? FailureReason,
    bool Replayed)
{
    public static CardTopupAcceptedResponse From(StartCardTopupResult result) =>
        new(
            result.CardTopup.Id,
            result.CardTopup.WalletId,
            result.CardTopup.State.ToText(),
            result.CardTopup.Amount,
            result.CardTopup.Currency,
            result.CardTopup.PaymentUrl,
            result.CardTopup.ExpiresAt,
            result.CardTopup.FailureReason,
            result.Replayed);
}

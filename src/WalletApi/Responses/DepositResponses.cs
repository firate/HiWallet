using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Domain.Deposits;

namespace HiWallet.WalletApi.Responses;

/// <summary>
/// Havaleyle yükleme bilgisi: bu IBAN'a, alıcı adı bu, açıklamaya bu numara. Havale
/// yalnızca müşterinin kendi adına kayıtlı hesabından kabul ediliyor.
/// </summary>
/// <param name="Reference">Hesap numarası: açıklamaya yazılacak.</param>
public sealed record DepositInstructionsResponse(string Iban, string AccountHolder, string Reference, string Currency);

/// <param name="Id">Askı kaydının ledger işlemi.</param>
/// <param name="Reason">Askıya alınma sebebi, metin olarak (<c>no_account_number</c>, <c>sender_not_holder</c> ...).</param>
/// <param name="AccountId">Açıklamadaki numaranın hesabı, bulunduysa.</param>
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
    DateTimeOffset CreatedAt)
{
    public static SuspendedDepositResponse From(SuspendedDepositView view) => new(
        view.Id,
        view.Provider,
        view.BankReference,
        view.Amount.Amount,
        view.Amount.Currency.Code,
        view.Reason.ToText(),
        view.AccountId,
        view.AccountNumber?.Value,
        view.ReceivedAt,
        view.CreatedAt);
}

public sealed record SuspendedDepositsResponse(IReadOnlyList<SuspendedDepositResponse> Items, int Size, Guid? NextCursor)
{
    public static SuspendedDepositsResponse From(SuspendedDepositPage page) =>
        new([.. page.Items.Select(SuspendedDepositResponse.From)], page.Size, page.NextCursor);
}

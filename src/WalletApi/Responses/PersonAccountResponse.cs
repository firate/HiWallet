using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Responses;

/// <param name="WalletId">Açılışla birlikte açılan ilk TRY cüzdanı; TRY'nin varsayılanı.</param>
public sealed record PersonAccountResponse(
    Guid AccountId, string AccountNumber, KycLevel KycLevel, Guid WalletId, DateTimeOffset CreatedAt)
{
    public static PersonAccountResponse From(OpenPersonAccountResult result) =>
        new(result.AccountId, result.Number.Value, result.KycLevel, result.WalletId, result.CreatedAt);
}

public sealed record KycLevelResponse(Guid AccountId, KycLevel KycLevel)
{
    public static KycLevelResponse From(KycLevelResult result) => new(result.AccountId, result.KycLevel);
}

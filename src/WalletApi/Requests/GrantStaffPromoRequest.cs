using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.WalletApi.Requests;

/// <param name="Scope"><c>all_businesses</c> ya da <c>selected_businesses</c>.</param>
/// <param name="MerchantAccountIds">Seçili işyerleri kapsamında işyeri hesapları; her yerde geçerli kapsamda boş.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
public sealed record GrantStaffPromoRequest(
    decimal Amount,
    string Currency,
    string Scope,
    IReadOnlyList<Guid>? MerchantAccountIds,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>Cüzdan yoldan, çalışan token'dan, anahtar başlıktan geliyor.</summary>
    public GrantStaffPromoCommand ToCommand(Guid walletId, string employeeSubject, string idempotencyKey) =>
        new(walletId, Amount, Currency, PromoTexts.ScopeFromText(Scope), MerchantAccountIds ?? [],
            ExpiresAt, employeeSubject, idempotencyKey);
}

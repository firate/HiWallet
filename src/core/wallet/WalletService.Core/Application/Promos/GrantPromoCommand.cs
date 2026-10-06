namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// İşyerinin kendi müşterisine promo vermesi (decisions.md madde 37). İşyeri
/// <c>cash</c> kovasından fonluyor; parti yalnızca işyerinin kendisinde geçerli.
/// </summary>
/// <param name="FunderWalletId">İşyerinin cüzdanı. Parayı bu cüzdan veriyor.</param>
/// <param name="WalletId">Promo'yu alan cüzdan.</param>
/// <param name="ExpiresAt">Opsiyonel. Verilmezse parti süresiz.</param>
/// <param name="IdempotencyKey">
/// ZORUNLU (decisions.md madde 4); para hareket ettiriyor. Kapsam fonlayan cüzdan.
/// </param>
public sealed record GrantPromoCommand(
    Guid FunderWalletId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    string IdempotencyKey);

/// <param name="Replayed">
/// <c>true</c> ise bu anahtar daha önce işlenmişti; yeni parti açılmadı.
/// </param>
public sealed record GrantPromoResult(Guid GrantId, bool Replayed);

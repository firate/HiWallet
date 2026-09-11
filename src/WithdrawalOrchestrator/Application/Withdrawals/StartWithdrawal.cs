using HiWallet.Shared.Contracts.Actors;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// Çekim başlatma isteği. <see cref="Destination"/> zaten <see cref="Iban"/> —
/// doğrulama sınırda yapıldı ve buraya string olarak GELMİYOR (CLAUDE.md
/// "Withdrawal saga").
/// </summary>
/// <param name="AccountId">
/// Idempotency kapsamı. Orchestrator bunu cüzdandan TÜRETEMEZ: cüzdan tablosu
/// wallet'ta ve servis sınırı geçilmiyor. Bu yüzden istekle birlikte geliyor.
/// </param>
/// <param name="Amount">Müşterinin çekmek istediği tutar. Komisyon HARİÇ — onu wallet ekliyor.</param>
/// <param name="InitiatedBy">
/// Çekimi kim istedi (decisions.md madde 34). <see cref="AccountId"/>'den ayrı:
/// hesap "parası kimin", bu alan "kim istedi". Backoffice müşteri adına çekim
/// açtığında ikisi farklı olur ve kalıcı kayıtta çalışanın izi kalır.
/// </param>
public sealed record StartWithdrawalCommand(
    Guid AccountId,
    Guid WalletId,
    decimal Amount,
    string Currency,
    Iban Destination,
    string IdempotencyKey,
    CommandActor InitiatedBy);

/// <param name="Replayed">
/// Aynı <c>(hesap, idempotency key)</c> ikilisiyle daha önce başlatılmış. Yeni saga
/// AÇILMADI, yeni komut da gönderilmedi; dönen kimlik mevcut saga'nın.
/// </param>
public sealed record StartWithdrawalResult(Guid WithdrawalId, WithdrawalState State, bool Replayed);

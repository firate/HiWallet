using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.Application.Transfers;

/// <summary>
/// Cüzdanlar arası para hareketi. Beş transfer tipi de aynı çekirdekten geçer;
/// tip yalnızca policy katmanını değiştirir (overview.md madde 4).
/// </summary>
/// <param name="FromWalletId">Gönderen cüzdan. Hesap değil CÜZDAN kimliği.</param>
/// <param name="ToWalletId">Alan cüzdan.</param>
/// <param name="Amount">
/// Alıcıya geçecek tutar. Komisyon buna EK olarak gönderenden düşülür — alıcı her zaman
/// tam <paramref name="Amount"/> alır.
/// </param>
/// <param name="IdempotencyKey">
/// Client üretir ve ZORUNLU (decisions.md madde 4). Aynı key ile ikinci istek yeni
/// transfer YAPMAZ, mevcut işlemi döner. Kapsam gönderen cüzdan.
///
/// Varsayılanı YOK: opsiyonel olsaydı anahtarsız bir istek sessizce geçer ve o
/// transferin tekrarı hiçbir şeye takılmadan ikinci kez yazılırdı.
/// </param>
public sealed record CreateTransferCommand(
    Guid FromWalletId,
    Guid ToWalletId,
    decimal Amount,
    string Currency,
    TransferType Type,
    string IdempotencyKey);

/// <param name="Replayed">
/// <c>true</c> ise bu istek daha önce işlenmişti; yeni bir şey yazılmadı, mevcut
/// işlemin kimliği dönüyor.
/// </param>
public sealed record TransferResult(Guid TransactionId, bool Replayed);

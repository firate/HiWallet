namespace HiWallet.WalletService.Application.Balances;

/// <summary>
/// Cüzdanın hareketleri, yeniden eskiye. Kaynak <c>ledger_entries</c> —
/// bakiye projeksiyonu değil, hareketin kendisi.
///
/// <b>Sayfalama cursor ile</b> (<c>baseline.md</c> madde 8). Ledger append-only ve
/// yeni satırlar listenin BAŞINA giriyor; offset kullanılsaydı iki sayfa arasında
/// gelen bir top-up sayfayı kaydırır ve müşteri aynı kaydı iki kez görürdü.
/// Ayrıca derin sayfada <c>OFFSET</c> okunup atılan satır demek, cursor ise
/// index üzerinde tek arama.
/// </summary>
/// <param name="After">
/// Önceki sayfanın son hareketinin kimliği. İlk sayfada <c>null</c>.
/// </param>
/// <param name="Size">
/// İstenen sayfa boyutu. <see cref="WalletMovementPage.MaxSize"/>'a çekiliyor;
/// sınırsız sorgu yok.
/// </param>
public sealed record GetWalletMovementsQuery(Guid WalletId, long? After, int Size);

/// <param name="MovementId">
/// <c>ledger_entries.id</c>. Aynı zamanda cursor — ikinci bir yüzey kimlik
/// üretilmiyor, sıra zaten bu kolonun sırası.
/// </param>
/// <param name="Amount">İşaret yön taşır: credit <c>+</c>, debit <c>-</c>.</param>
public sealed record WalletMovementView(
    long MovementId,
    Guid TransactionId,
    string Type,
    decimal Amount,
    string Currency,
    string FundType,
    DateTimeOffset CreatedAt);

/// <param name="NextCursor">
/// Bir sonraki sayfa isteğinde <c>after</c> olarak gönderilecek değer. Son
/// sayfada <c>null</c> — istemci başka sayfa olmadığını buradan anlıyor, toplam
/// sayı sorgusu koşmadan.
/// </param>
public sealed record WalletMovementPage(
    IReadOnlyList<WalletMovementView> Items,
    int Size,
    long? NextCursor)
{
    /// <summary>
    /// Sayfa başına tavan. Daha büyüğü isteyen request reddedilmiyor, buraya
    /// çekiliyor: istemciyi kırmadan sunucuyu koruyor.
    /// </summary>
    public const int MaxSize = 100;

    public const int DefaultSize = 50;
}

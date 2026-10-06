namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Tüketilmiş mesajların kaydı. İş kavramı değil, mesajlaşma defteri — bu yüzden
/// Domain'de değil burada.
///
/// İki kademe idempotency'nin ikincisi (overview.md madde 5): birincisi webhook
/// girişindeki inbox <c>(provider, event_id)</c> UNIQUE, bu ise tüketici tarafı.
/// Broker en az bir kez teslim ediyor; aynı mesaj iki kez gelirse ikincisi buraya
/// takılıyor ve ledger'a dokunulmuyor.
///
/// <b><c>ledger_transactions</c> üzerindeki unique index'le çakışmıyor mu?</b> Hayır,
/// ikisi farklı soruya cevap veriyor. Oradaki "bu hesap için bu idempotency key'iyle
/// bir işlem yazıldı mı", buradaki "bu MESAJ işlendi mi". Ledger'a hiç yazmadan
/// sonlanan bir mesaj (örneğin sıfır tutar) orada iz bırakmaz, burada bırakır.
/// </summary>
internal sealed class ProcessedEvent
{
    public required string Provider { get; init; }

    public required string EventId { get; init; }

    public required DateTimeOffset ProcessedAt { get; init; }

    /// <summary>
    /// Ledger'a yazıldıysa hangi işlem. Tekrar gelen mesajda orijinal işlem kimliği
    /// buradan dönülüyor — yeniden hesaplamaya gerek kalmıyor.
    /// </summary>
    public Guid? LedgerTransactionId { get; init; }
}

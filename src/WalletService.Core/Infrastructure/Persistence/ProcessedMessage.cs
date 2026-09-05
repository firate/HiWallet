namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// İşlenmiş KOMUTLARIN defteri. <see cref="ProcessedEvent"/>'ten ayrı tablo, çünkü
/// anahtarları farklı: orada <c>(provider, event_id)</c> — sağlayıcının ürettiği bir
/// dize; burada <c>CommandId</c> — bizim ürettiğimiz bir guid. İkisini tek tabloda
/// birleştirmek ya sahte bir "provider" değeri ya da nullable anahtar isterdi.
///
/// <b>Cevabı da saklıyor, bilerek.</b> Akış şu: komut gelir → ledger yazılır (commit)
/// → cevap event'i yayınlanır → mesaj ack'lenir. Yayın başarısızsa ack yok ve komut
/// yeniden teslim ediliyor; o teslimde ledger'a İKİNCİ KEZ yazılmamalı ama cevap yine
/// gitmeli. Cevap saklanmasaydı ikinci teslim sessizce ack'lenir ve orchestrator'ın
/// saga'sı sonsuza kadar beklerdi.
///
/// <b>Bu bir outbox DEĞİL.</b> Outbox'ta satırı arka planda bir relay tarayıp
/// yayınlar; burada yayın tüketici iş parçacığında, aynı teslimde yapılıyor ve
/// yeniden deneme broker'ın redelivery'sinden geliyor (decisions.md madde 32).
/// Tarayan bir şey yok.
/// </summary>
internal sealed class ProcessedMessage
{
    /// <summary>Komutun <c>CommandId</c>'si. Orchestrator'ın outbox satır kimliğiyle aynı değer.</summary>
    public required Guid MessageId { get; init; }

    /// <summary>Hangi komuttu. Yalnızca teşhis için; akış buna dallanmıyor.</summary>
    public required string MessageType { get; init; }

    /// <summary>İlişkili saga. "Bu saga'ya ne cevap verdik" sorgusu için.</summary>
    public required Guid SagaId { get; init; }

    public required DateTimeOffset ProcessedAt { get; init; }

    /// <summary>
    /// Ledger'a yazıldıysa hangi işlem. Reddedilen komutta NULL — hiç para hareketi
    /// olmadı.
    /// </summary>
    public Guid? LedgerTransactionId { get; init; }

    /// <summary>Yayınlanan cevabın routing key'i, yani event tipinin adı.</summary>
    public required string ReplyRoutingKey { get; init; }

    /// <summary>Yayınlanan cevabın JSON hali. Tekrar teslimde birebir bu gönderiliyor.</summary>
    public required string ReplyPayload { get; init; }
}

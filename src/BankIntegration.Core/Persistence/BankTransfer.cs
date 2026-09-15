namespace HiWallet.BankIntegration.Persistence;

/// <summary>
/// "Bankaya ne gönderdik, ne öğrendik" kaydı. Bizim tarafımızın doğruluk kaynağı;
/// bankanın kendi kaydıyla karıştırılmamalı.
///
/// <b>Üç işi birden görüyor</b> ve bu bilinçli:
/// <list type="number">
/// <item>Komut deduplikasyonu — <c>CommandId</c> PK, ikinci teslim transferi
/// tekrarlamıyor. Ayrı bir <c>processed_messages</c> tablosu AÇILMIYOR: bu zaten
/// "ne yaptık" kaydı ve anahtarı da aynı (CLAUDE.md "Withdrawal saga").</item>
/// <item>Bekleyen transfer listesi — mutabakat taraması buradan okuyor.</item>
/// <item>Outbox — üretilen cevap burada saklanıyor ve yayını ayrı bir relay
/// yapıyor.</item>
/// </list>
///
/// Üçünü tek satırda tutmak, "sonucu öğrendim" ile "cevabı yayınladım" arasındaki
/// farkın kaybolmamasını sağlıyor: ikisi ayrı kolon, ikisi ayrı an.
/// </summary>
public sealed class BankTransfer
{
    /// <summary>
    /// Orchestrator'ın komut kimliği; aynı zamanda <c>withdrawal_outbox.id</c>.
    /// Bankaya idempotency anahtarı olarak da bu gidiyor — ikinci bir yüzey id
    /// üretilmiyor, yoksa relay aynı satırı iki kez yayınladığında banka iki ayrı
    /// transfer açardı.
    /// </summary>
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Hedef IBAN. Log'a MASKELİ yazılıyor ama kolonda tam duruyor: ihtilafta
    /// "nereye gönderdik" sorusunun cevabı bu.
    /// </summary>
    public required string DestinationIban { get; init; }

    public required string Status { get; set; }

    /// <summary>
    /// Bankanın referansı. Banka komutu kabul edene kadar NULL — kabul edilmemiş
    /// bir transferin referansı yok.
    ///
    /// Durum sorgusunun anahtarı bu; NULL kalırsa mutabakat taraması o satır için
    /// soracak bir şey bulamaz.
    /// </summary>
    public string? BankReference { get; set; }

    /// <summary>Bankanın kestiği ücret. Sonuç öğrenilene kadar NULL.</summary>
    public decimal? Fee { get; set; }

    /// <summary>Yalnızca kalıcı başarısızlıkta dolu. Müşteriye gösterilebilir.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Bankayı ARADIĞIMIZ an. Mutabakat taramasının "ne zamandır bekliyor" ölçüsü.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Sonucu ÖĞRENDİĞİMİZ an. NULL ise transfer hâlâ bekliyor.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// Sonucu hangi yolun getirdiği: <c>callback</c> ya da <c>reconciliation</c>.
    /// İşleyişe etkisi YOK, tamamen gözlem için — ama önemli bir gözlem: bu kolonda
    /// <c>reconciliation</c> oranının artması callback hattının bozulduğunu söyler
    /// (decisions.md madde 35).
    /// </summary>
    public string? ResolvedVia { get; set; }

    /// <summary>
    /// Yayınlanacak cevabın routing key'i. Saklanıyor çünkü tekrar teslimde iş
    /// ikinci kez yapılmadan AYNI cevap yeniden yayınlanmak zorunda.
    /// </summary>
    public string? ReplyRoutingKey { get; set; }

    public string? ReplyPayload { get; set; }

    /// <summary>
    /// Cevabın broker'a verildiği an. NULL ve <see cref="ResolvedAt"/> dolu ise
    /// relay'in işi bitmemiş — satır outbox kuyruğunda demek.
    /// </summary>
    public DateTimeOffset? ReplyPublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }
}

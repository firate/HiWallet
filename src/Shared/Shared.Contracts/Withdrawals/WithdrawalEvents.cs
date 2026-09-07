namespace HiWallet.Shared.Contracts.Withdrawals;

// Event'ler: olmuş bitmiş bir olgunun duyurusu. Geçmiş zaman adlandırılıyor
// (structure.md "Adlandırma") — "yap" değil "oldu".
//
// CommandId taşımıyorlar; korelasyon SagaId üzerinden. Saga bunları kendi
// durumuyla karşılaştırıp uyguluyor, yok sayıyor ya da çelişki olarak
// işaretliyor (decisions.md madde 31).

/// <summary>
/// wallet → orchestrator. Para düşüldü, "yolda" (clearing'de).
/// </summary>
public sealed record WithdrawalDebited
{
    public required Guid SagaId { get; init; }

    public required Guid LedgerTransactionId { get; init; }

    /// <summary>
    /// Cüzdandan çıkan TOPLAM: müşterinin istediği tutar + komisyon. Orchestrator
    /// komisyonu hesaplamıyor, sonucu buradan öğreniyor.
    /// </summary>
    public required decimal TotalDebited { get; init; }
}

/// <summary>
/// wallet → orchestrator. Düşme yapılmadı — yetersiz bakiye ya da limit aşımı.
/// HİÇBİR para hareketi olmadı; saga doğrudan terminal duruma gidiyor.
///
/// Limit kontrolü burada, orchestrator'da değil: günlük limit hesap bazında
/// uygulanıyor ve o veri wallet'ta (decisions.md madde 20). Orchestrator'da
/// tekrarlansaydı iki ayrı doğruluk kaynağı olurdu.
/// </summary>
public sealed record WithdrawalDebitRejected
{
    public required Guid SagaId { get; init; }

    /// <summary>Müşteriye gösterilebilir sebep. İç detay ya da bakiye içermez.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// bank-service → orchestrator. Transfer tamamlandı.
/// </summary>
public sealed record BankTransferSucceeded
{
    public required Guid SagaId { get; init; }

    /// <summary>Bankanın kendi referansı. Mutabakatta eşleştirme için taşınıyor.</summary>
    public required string BankReference { get; init; }

    /// <summary>
    /// Bankanın bu transfer için kestiği ücret.
    ///
    /// Top-up'takinin AKSİNE ayrı bir settlement bildirimi beklenmiyor: transferi
    /// yapan taraf ücreti o anda biliyor ve para nostro'dan o anda çıkıyor. Ayrı
    /// bir batch bildirimi kurgulamak, bu sistemde karşılığı olmayan bir adım
    /// olurdu — banka burada webhook gönderen bir sağlayıcı değil, mesajlaşan bir
    /// servis (decisions.md madde 13, adım 5.5b).
    /// </summary>
    public required decimal FeeAmount { get; init; }
}

/// <summary>
/// bank-service → orchestrator. Transfer KALICI olarak başarısız.
///
/// Geçici hatalar (timeout, ağ) bank-service'in içinde yeniden deneniyor; bu event
/// ancak denemeler tükendiğinde çıkıyor. Ayrım önemli — her geçici hatada telafi
/// başlatmak müşterinin parasını gereksiz yere ileri geri taşırdı
/// (overview.md madde 6).
/// </summary>
public sealed record BankTransferFailed
{
    public required Guid SagaId { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// wallet → orchestrator. Ters kayıt yazıldı, müşterinin parası geri verildi.
/// </summary>
public sealed record WithdrawalRefunded
{
    public required Guid SagaId { get; init; }

    /// <summary>Ters kaydın ledger işlemi. Orijinal işlem SİLİNMİYOR, yenisi yazılıyor.</summary>
    public required Guid LedgerTransactionId { get; init; }
}

/// <summary>
/// wallet → orchestrator. Çekimin muhasebesi kapandı: clearing boşaldı, para
/// nostro'dan çıktı.
///
/// Saga ancak bunu aldıktan sonra <c>completed</c> oluyor. Banka başarılı dediğinde
/// bitirmek daha basit olurdu ama settlement kaydını yazacak bir şey kalmazdı:
/// terminal duruma gelmiş saga'yı takılmış saga taraması da görmez ve clearing
/// sessizce açık kalırdı.
/// </summary>
public sealed record WithdrawalSettled
{
    public required Guid SagaId { get; init; }

    /// <summary>Settlement kaydının ledger işlemi.</summary>
    public required Guid LedgerTransactionId { get; init; }
}

using HiWallet.Shared.Contracts.Actors;

namespace HiWallet.Shared.Contracts.Withdrawals;

// Komutlar: bir servisten diğerine "şunu yap" talimatı. Event'lerden ayrı
// dosyada çünkü davranışları farklı — komut TEK bir alıcıya gidiyor ve
// CommandId ile deduplike ediliyor, event olmuş bitmiş bir olguyu duyuruyor.
//
// Hepsinde CommandId var: alıcı aynı komutu iki kez işlemiyor (processed_messages,
// overview.md madde 6). SagaId ise korelasyon — cevap hangi saga'ya ait.
//
// Alanlar init; yeni alan eklemek eski üreticileri bozmuyor.

/// <summary>
/// orchestrator → wallet. "Bu cüzdandan çekim için para düş."
///
/// Komisyonu ve limiti WALLET hesaplıyor, orchestrator göndermiyor: politika
/// wallet'ın bilgisi ve iki serviste tekrarlanırsa ilk sapmada sessizce ayrışır.
/// Bu yüzden komut yalnızca müşterinin istediği tutarı taşıyor.
/// </summary>
public sealed record DebitForWithdrawal
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    public required Guid WalletId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    /// Çekimi kim başlattı (madde 34). Normalde müşterinin kendisi; backoffice
    /// müşteri adına çekim açarsa çalışan. Wallet bunu ledger'a yazıyor — türetmiyor,
    /// çünkü cüzdandan türetmek backoffice'in açtığı çekimi müşteri yapmış gibi
    /// gösterirdi.
    /// </summary>
    public required CommandActor Actor { get; init; }
}

/// <summary>
/// orchestrator → bank-service. "Bu tutarı şu IBAN'a gönder."
///
/// IBAN burada <c>string</c>: doğrulanmış <c>Iban</c> tipi orchestrator'ın domain'inde
/// ve sözleşme paketine domain tipi girmez. Doğrulama sınırda yapıldı; bu noktadan
/// sonra taşınan şey artık doğrulanmış bir değer.
/// </summary>
public sealed record StartBankTransfer
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string DestinationIban { get; init; }
}

/// <summary>
/// orchestrator → wallet. "Bu çekimi geri al."
///
/// Tutar TAŞIMIYOR, bilerek. Ters kayıt orijinal işlemin aynası olmak zorunda
/// (üç bacak: cüzdan, clearing, revenue) ve o işlemi wallet yazdı — tutarı
/// orchestrator'dan geri göndermek ikinci bir doğruluk kaynağı yaratır ve iki
/// taraf ayrışırsa ledger dengesiz kalır. Wallet kendi yazdığını tersine çeviriyor.
/// </summary>
public sealed record RefundWithdrawal
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    /// <summary>
    /// Telafiyi kim tetikledi (madde 34). Bankanın reddinde <c>system</c> — kimse
    /// istemedi, saga karar verdi. Backoffice'ten iptal edildiğinde o çalışan.
    ///
    /// <b>Bu alan maddenin en çok işe yaradığı yer.</b> Taşınmasaydı, parayı geri
    /// vermeye karar veren insan ledger'da <c>system</c> olarak görünürdü ve kalıcı
    /// kayıtta hiçbir izi kalmazdı.
    /// </summary>
    public required CommandActor Actor { get; init; }
}

/// <summary>
/// orchestrator → wallet. "Bu çekimin muhasebesini kapat."
///
/// Ücret TAŞIYOR, tutar taşımıyor — <see cref="RefundWithdrawal"/>'ın tam tersi
/// gerekçeyle. Çekim tutarını wallet zaten kendi yazdı ve clearing'de duruyor;
/// ücreti ise yalnızca banka biliyor ve buraya ondan geliyor. Her iki durumda da
/// kural aynı: bilgiyi kim üretiyorsa o taşıyor, ikinci kaynak açılmıyor.
/// </summary>
public sealed record SettleWithdrawal
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    /// <summary>Bankanın kestiği ücret. Sıfır olabilir.</summary>
    public required decimal FeeAmount { get; init; }

    /// <summary>Bankanın referansı; provider_fees eşleştirmesinin anahtarı.</summary>
    public required string BankReference { get; init; }
}

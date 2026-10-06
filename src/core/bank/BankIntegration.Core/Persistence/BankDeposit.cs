namespace HiWallet.BankIntegration.Persistence;

/// <summary>
/// Banka hesabımıza gelmiş havale: "bankada ne gördük" kaydı. Bankanın bildiriminden ya
/// da hesap hareketi taramasından yazılıyor; ikisi aynı havaleyi getirdiğinde ikincisi
/// yazılmıyor (<c>(provider, bank_reference)</c> UNIQUE).
///
/// <b>İki işi birden görüyor</b>, <c>bank_transfers</c> gibi: havalenin kaydı ve outbox.
/// Wallet'a gidecek mesaj yazılırken üretilip saklanıyor, yayını relay yapıyor. Ayrı
/// tablo olsaydı "havaleyi gördük" ile "wallet'a bildirdik" iki ayrı transaction'a
/// düşerdi.
///
/// <b>Gönderenin kişisel verisi BURADA</b>: adı, IBAN'ı ve kimlik numarası. Wallet'a
/// yalnızca kimlik numarası gidiyor (gönderenin hesap sahibi olduğunu doğrulamak için) ve
/// orada yazılmıyor. Kaynağa iade bu satırdaki IBAN'a yapılacak.
/// </summary>
public sealed class BankDeposit
{
    /// <summary>Wallet'a giden mesajın kimliği de bu: tekrar yayında aynı kalıyor.</summary>
    public required Guid Id { get; init; }

    /// <summary>Parayı alan banka; wallet'taki <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public required string Provider { get; init; }

    /// <summary>Bankanın gelen işlem referansı. Banka başına tekil.</summary>
    public required string BankReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Gönderenin yazdığı açıklama, olduğu gibi.</summary>
    public string? Description { get; init; }

    public string? SenderName { get; init; }

    /// <summary>Kolonda tam duruyor, log'a MASKELİ yazılıyor: iade bu IBAN'a.</summary>
    public string? SenderIban { get; init; }

    public string? SenderNationalId { get; init; }

    /// <summary>Paranın bankaya girdiği an, bankanın bildirdiği.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Bizim gördüğümüz an.</summary>
    public required DateTimeOffset DiscoveredAt { get; init; }

    /// <summary>
    /// Havaleyi hangi yolun getirdiği: <c>callback</c> ya da <c>reconciliation</c>.
    /// İşleyişe etkisi YOK; <c>reconciliation</c> oranının artması bildirim hattının
    /// bozulduğunu söyler.
    /// </summary>
    public required string DiscoveredVia { get; init; }

    /// <summary>Wallet'a gidecek mesaj, yazılırken üretilmiş hali. Tekrar yayında birebir aynı.</summary>
    public required string Payload { get; init; }

    /// <summary>Mesajın broker'a verildiği an. NULL ise relay'in işi bitmemiş.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public int PublishAttempts { get; set; }

    public string? LastError { get; set; }
}

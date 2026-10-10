namespace HiWallet.WalletService.Domain.Deposits;

/// <summary>Askıdaki havalenin çözümündeki bir adım.</summary>
public enum DepositResolutionKind
{
    /// <summary>Bir hesabın varsayılan cüzdanına aktarıldı — son adım.</summary>
    Moved = 1,

    /// <summary>İade başladı: askıdan düşüldü, para bankaya gidiyor.</summary>
    ReturnStarted = 2,

    /// <summary>İade tamamlandı: para göndericiye gitti — son adım.</summary>
    Returned = 3,

    /// <summary>Banka iadeyi reddetti: para askıya döndü, havale yeniden karara açık.</summary>
    ReturnFailed = 4
}

public static class DepositResolutionKinds
{
    /// <summary>Kolon değeri; CHECK ve sorgular aynı metinleri kullanıyor.</summary>
    public static string ToText(this DepositResolutionKind kind) => kind switch
    {
        DepositResolutionKind.Moved => "moved",
        DepositResolutionKind.ReturnStarted => "return_started",
        DepositResolutionKind.Returned => "returned",
        DepositResolutionKind.ReturnFailed => "return_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Eşlemesi yazılmamış adım.")
    };

    public static DepositResolutionKind FromText(string text) => text switch
    {
        "moved" => DepositResolutionKind.Moved,
        "return_started" => DepositResolutionKind.ReturnStarted,
        "returned" => DepositResolutionKind.Returned,
        "return_failed" => DepositResolutionKind.ReturnFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen adım.")
    };
}

/// <summary>
/// Askıdaki havalenin çözümünün bir adımı. Havale başına sıralı satırlar (anahtar havale ve
/// sıra); son satır havalenin şu anki halini söylüyor: hiç satır yoksa ya da son iade geri
/// konduysa havale karara açık. Satır kapı: kararın ledger işlemiyle aynı transaction'da,
/// ondan önce yazılıyor ve aynı sırayı alan ikinci karar anahtara takılıyor. Değişmiyor ve
/// silinmiyor (veritabanında REVOKE).
/// </summary>
public sealed class SuspendedDepositResolution
{
    private SuspendedDepositResolution()
    {
    }

    /// <summary>Havalenin askı kaydı: <see cref="SuspendedDeposit.LedgerTransactionId"/>.</summary>
    public Guid SuspendedDepositId { get; private set; }

    /// <summary>Havale içinde adımın sırası, 1'den.</summary>
    public int Seq { get; private set; }

    public DepositResolutionKind Kind { get; private set; }

    /// <summary>
    /// Adımın ledger işlemi: aktarımda cüzdan +/askı −, iadenin başlamasında askı −/clearing +,
    /// tamamlanmasında kapanış, reddinde askıya geri koyma.
    /// </summary>
    public Guid LedgerTransactionId { get; private set; }

    /// <summary>Paranın aktarıldığı hesap; yalnızca aktarımda dolu.</summary>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// Adımı başlatan: kararlarda çalışanın <c>sub</c>'ı, bankanın sonucuna verilen
    /// tepkilerde iade akışının adı. Ledger'daki aktörle aynı.
    /// </summary>
    public string ResolvedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Havale bu adımdan sonra karara açık mı.</summary>
    public static bool IsOpenAfter(DepositResolutionKind? last) =>
        last is null or DepositResolutionKind.ReturnFailed;
}

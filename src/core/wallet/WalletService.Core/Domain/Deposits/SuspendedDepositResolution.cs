namespace HiWallet.WalletService.Domain.Deposits;

/// <summary>Askıdaki havale için verilen karar.</summary>
public enum DepositResolutionKind
{
    /// <summary>Bir hesabın varsayılan cüzdanına aktarıldı.</summary>
    Moved = 1
}

/// <summary>
/// Askıdaki havalenin çözüldüğünün kaydı. Havale başına TEK satır (anahtar havalenin
/// kendisi): aynı para ikinci kez aktarılamıyor, eşzamanlı iki karardan biri anahtara
/// takılıyor. Satır kararın ledger işlemiyle aynı transaction'da, ondan önce kapı olarak
/// yazılıyor; değişmiyor ve silinmiyor (veritabanında REVOKE). Kararı veren çalışan
/// ledger'daki aktörle aynı.
/// </summary>
public sealed class SuspendedDepositResolution
{
    private SuspendedDepositResolution()
    {
    }

    /// <summary>Çözülen havale: <see cref="SuspendedDeposit.LedgerTransactionId"/>.</summary>
    public Guid SuspendedDepositId { get; private set; }

    public DepositResolutionKind Kind { get; private set; }

    /// <summary>Kararın ledger işlemi: aktarımda cüzdan +, askı −.</summary>
    public Guid LedgerTransactionId { get; private set; }

    /// <summary>Paranın aktarıldığı hesap.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Kararı veren çalışanın <c>sub</c>'ı.</summary>
    public string ResolvedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}

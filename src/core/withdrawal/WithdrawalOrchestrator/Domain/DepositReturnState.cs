namespace HiWallet.WithdrawalOrchestrator.Domain;

/// <summary>Askıdaki havalenin iadesinin durumları. Sayılar sabit; veritabanında metin.</summary>
public enum DepositReturnState
{
    /// <summary>İstek alındı, havale henüz askıdan düşülmedi.</summary>
    Initiated = 1,

    /// <summary>Wallet düşmedi: havale yok, aktarılmış ya da iadesi sürüyor — terminal.</summary>
    Rejected = 2,

    /// <summary>Askıdan düşüldü, para clearing'de; banka sonucu bekleniyor.</summary>
    BankTransferPending = 3,

    /// <summary>Para göndericiye gitti, muhasebesi kapanıyor.</summary>
    Settling = 4,

    /// <summary>İade tamamlandı — terminal.</summary>
    Completed = 5,

    /// <summary>Banka kalıcı olarak reddetti; para askıya geri konuyor.</summary>
    Restoring = 6,

    /// <summary>İade gitmedi, para yeniden askıda ve havale karara açık — terminal.</summary>
    Failed = 7
}

public static class DepositReturnStates
{
    public static bool IsTerminal(this DepositReturnState state) =>
        state is DepositReturnState.Rejected or DepositReturnState.Completed or DepositReturnState.Failed;

    /// <summary>
    /// Devam eden durumlar; kısmi index'in filtresi ve takılmış saga taraması bundan. İadede
    /// bir insanı bekleyen durum yok: devam eden her iade sistemi bekliyor.
    /// </summary>
    public static IEnumerable<DepositReturnState> Active =>
        Enum.GetValues<DepositReturnState>().Where(state => !state.IsTerminal());

    /// <summary>Kolon, HTTP cevabı ve index filtresi aynı kaynaktan (<see cref="WithdrawalStates.ToText"/>).</summary>
    public static string ToText(this DepositReturnState state) => state switch
    {
        DepositReturnState.Initiated => "initiated",
        DepositReturnState.Rejected => "rejected",
        DepositReturnState.BankTransferPending => "bank_transfer_pending",
        DepositReturnState.Settling => "settling",
        DepositReturnState.Completed => "completed",
        DepositReturnState.Restoring => "restoring",
        DepositReturnState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Eşlemesi yazılmamış iade durumu.")
    };

    public static DepositReturnState FromText(string text) => text switch
    {
        "initiated" => DepositReturnState.Initiated,
        "rejected" => DepositReturnState.Rejected,
        "bank_transfer_pending" => DepositReturnState.BankTransferPending,
        "settling" => DepositReturnState.Settling,
        "completed" => DepositReturnState.Completed,
        "restoring" => DepositReturnState.Restoring,
        "failed" => DepositReturnState.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen iade durumu.")
    };
}

/// <summary>Saga'nın kendi koyduğu sebep adları; reddetmede ad wallet'tan geliyor.</summary>
public static class DepositReturnFailureRules
{
    /// <summary>Banka iadeyi kabul etmedi; para askıya geri döndü.</summary>
    public const string BankRejected = "bank_rejected";
}

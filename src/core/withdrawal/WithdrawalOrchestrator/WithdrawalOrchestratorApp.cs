namespace HiWallet.WithdrawalOrchestrator;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;WithdrawalOrchestratorApp&gt;</c>.
///
/// wallet-service'teki gibi <c>public partial class Program</c> KULLANILAMIYOR:
/// üst düzey deyimlerin ürettiği <c>Program</c> global isim alanında duruyor ve
/// iki servisi aynı test projesinden referans veren derleme CS0433 ile patlıyor.
/// İsim alanı olan bir işaretçi tipi bunu çözüyor — fabrika yalnızca tipin
/// assembly'sine bakıyor, adına değil.
/// </summary>
public sealed class WithdrawalOrchestratorApp;

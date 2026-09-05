namespace HiWallet.BankService;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;BankServiceApp&gt;</c>.
///
/// Üst düzey deyimlerin ürettiği <c>Program</c> tipi global isim alanında; birden
/// fazla host aynı test projesinden referans verildiğinde çakışıyor (CS0433).
/// İsim alanı olan bir işaretçi tipi bunu çözüyor — fabrika yalnızca tipin
/// assembly'sine bakıyor, adına değil.
/// </summary>
public sealed class BankServiceApp;

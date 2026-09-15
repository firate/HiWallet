namespace HiWallet.BankAdapter;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;BankAdapterApp&gt;</c>.
///
/// Üst düzey deyimlerin ürettiği <c>Program</c> tipi global isim alanında; birden
/// fazla host aynı test projesinden referans verildiğinde çakışıyor (CS0433).
/// İsim alanı olan bir işaretçi tipi bunu çözüyor.
/// </summary>
public sealed class BankAdapterApp;

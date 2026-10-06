namespace HiWallet.BankWebhook;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;BankWebhookApp&gt;</c>.
///
/// Üst düzey deyimlerin ürettiği <c>Program</c> tipi global isim alanında; birden
/// fazla host aynı test projesinden referans verildiğinde çakışıyor (CS0433).
/// </summary>
public sealed class BankWebhookApp;

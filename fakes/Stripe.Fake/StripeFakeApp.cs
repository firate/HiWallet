namespace HiWallet.Stripe.Fake;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;StripeFakeApp&gt;</c>.
///
/// Üst düzey deyimlerin ürettiği <c>Program</c> tipi global isim alanında; birden
/// fazla host aynı test projesinden referans verildiğinde çakışıyor (CS0433).
/// </summary>
public sealed class StripeFakeApp;

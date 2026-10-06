namespace HiWallet.CardTopup;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;CardTopupApp&gt;</c>. Gerekçe <c>WithdrawalOrchestratorApp</c>'te:
/// üst düzey deyimlerin ürettiği <c>Program</c> tipi birden fazla host aynı test
/// projesinden referans verildiğinde çakışıyor.
/// </summary>
public sealed class CardTopupApp;

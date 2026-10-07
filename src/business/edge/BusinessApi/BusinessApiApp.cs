namespace HiWallet.BusinessApi;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;BusinessApiApp&gt;</c>.
///
/// <c>Program</c> KULLANILMIYOR: üst düzey deyimlerin ürettiği <c>Program</c> tipi
/// global isim alanında ve birden fazla host'u aynı test projesinden referans veren
/// derleme CS0433 ile patlıyor. Fabrika yalnızca tipin assembly'sine bakıyor.
/// </summary>
public sealed class BusinessApiApp;

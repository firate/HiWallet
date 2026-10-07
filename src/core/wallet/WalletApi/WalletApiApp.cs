namespace HiWallet.WalletApi;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;WalletApiApp&gt;</c>.
///
/// <c>Program</c> KULLANILMIYOR: .NET 10'da üst düzey deyimlerin ürettiği
/// <c>Program</c> tipi public ve global isim alanında. Birden fazla host'u aynı
/// test projesinden referans veren derleme CS0433 ile patlıyor. Fabrika yalnızca
/// tipin assembly'sine bakıyor, adına değil — isim alanı olan bir işaretçi yeterli.
/// </summary>
public sealed class WalletApiApp;

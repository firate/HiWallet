namespace HiWallet.WalletService;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;WalletServiceApp&gt;</c>.
///
/// <c>Program</c> KULLANILMIYOR: .NET 10'da üst düzey deyimlerin ürettiği
/// <c>Program</c> tipi public ve global isim alanında. İki servisi aynı test
/// projesinden referans veren derleme CS0433 ile patlıyor. Fabrika yalnızca tipin
/// assembly'sine bakıyor, adına değil — isim alanı olan bir işaretçi yeterli.
/// </summary>
public sealed class WalletServiceApp;

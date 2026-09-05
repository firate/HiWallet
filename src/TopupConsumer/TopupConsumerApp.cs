namespace HiWallet.TopupConsumer;

/// <summary>
/// Integration testler için giriş noktası işaretçisi:
/// <c>WebApplicationFactory&lt;TopupConsumerApp&gt;</c>. Gerekçe
/// <c>WalletApiApp</c> ile aynı — .NET 10'da üretilen <c>Program</c> tipi public
/// ve global isim alanında, birden fazla host aynı test projesinde çakışıyor.
/// </summary>
public sealed class TopupConsumerApp;

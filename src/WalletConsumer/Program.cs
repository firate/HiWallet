using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.WalletConsumer;
using HiWallet.WalletService.Setup;

// Ingress'i YOK. Hiçbir istemci buraya bağlanmıyor; mesajları kendisi kuyruktan
// çekiyor. Ledger'a yazan kod böylece public bir uygulamanın içinden çıkmış oluyor
// (decisions.md madde 28).
//
// Wallet-api ile AYNI kütüphaneyi (WalletService.Core) ve aynı tabloları kullanıyor.
// İkisi ayrı deployable ama tek kod tabanı — invariant'ı zorlayan mantığın ikinci
// bir kopyası yok (decisions.md madde 25).
const string ServiceName = "hiwallet-wallet-consumer";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken işlenmekte olan mesaj bitsin. Bitmezse ack'lenmez ve yeniden teslim
// edilir — kayıp değil, sadece gereksiz tekrar.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddHiWalletPersistence();
builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);

// Çekim tarifesi yalnızca burada: çekim komutlarını işleyen tek uygulama bu.
// Bölüm eksikse startup'ta patlıyor (baseline.md madde 1).
builder.Services.AddWithdrawalPolicy(builder.Configuration);

// Sağlayıcı ücret tarifeleri, aynı gerekçeyle yalnızca burada: top-up'ı işleyen
// tek uygulama bu ve provider_fees satırını o akış yazıyor (decisions.md madde 10).
builder.Services.AddProviderPolicy(builder.Configuration);

builder.Services.AddWalletConsumer(builder.Configuration);

var app = builder.Build();

app.ValidateWalletConsumerConfiguration();

// Tek HTTP yüzeyi bu. Controller yok, API dokümanı yok, rate limiter yok.
// Probe olmadan "process ayakta ama tüketici tıkanmış" durumu görünmez olurdu.
app.MapHiWalletHealthChecks();

app.Run();

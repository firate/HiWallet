using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.TopupConsumer;
using HiWallet.WalletService.Setup;

// Ingress'i YOK. Hiçbir istemci buraya bağlanmıyor; mesajları kendisi kuyruktan
// çekiyor. Ledger'a yazan kod böylece public bir uygulamanın içinden çıkmış oluyor
// (decisions.md madde 28).
//
// Wallet-api ile AYNI kütüphaneyi (WalletService.Core) ve aynı tabloları kullanıyor.
// İkisi ayrı deployable ama tek kod tabanı — invariant'ı zorlayan mantığın ikinci
// bir kopyası yok (decisions.md madde 25).
const string ServiceName = "hiwallet-topup-consumer";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken işlenmekte olan mesaj bitsin. Bitmezse ack'lenmez ve yeniden teslim
// edilir — kayıp değil, sadece gereksiz tekrar.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddHiWalletPersistence();
builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddHiWalletTopupConsumer(builder.Configuration);

var app = builder.Build();

app.ValidateTopupConsumerConfiguration();

// Tek HTTP yüzeyi bu. Controller yok, Swagger yok, rate limiter yok.
// Probe olmadan "process ayakta ama tüketici tıkanmış" durumu görünmez olurdu.
app.MapHiWalletHealthChecks();

app.Run();

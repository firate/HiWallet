using HiWallet.BankAdapter.Setup;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;

// BANKAYA BAKAN ADAPTÖR — BİZİM KODUMUZ, üretimde de koşuyor (decisions.md madde 35).
//
// Sahte olan taraf `Bank.Fake`; burada sahtelik yalnızca `Bank:BaseUrl` değerinde.
// Gerçek bankaya geçerken bu projede tek satır değişmiyor.
//
// INGRESS'İ YOK. Komutu kuyruktan alıyor, bankayı kendisi arıyor. Bankanın bizi
// çağırdığı uç AYRI bir deployable (`bank-webhook`) — maruziyetleri farklı ve
// farklı maruziyet aynı process'te birleşmiyor (madde 28).
const string ServiceName = "hiwallet-bank-adapter";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken işlenmekte olan komut bitsin. Bitmezse ack'lenmez ve yeniden teslim
// edilir; banka çağrısı idempotency anahtarı taşıdığı için para iki kez gitmiyor.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddBankAdapter(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.ValidateBankAdapterConfiguration();

// Yalnızca health check ucu. Controller YOK: bu servisin HTTP yüzeyi yok, dışarı çağrı
// yapıyor. Health check ucu da host'a açılmıyor, compose içinde kalıyor.
app.MapHiWalletHealthChecks();

app.Run();

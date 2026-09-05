using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;

// Withdrawal saga'sının state machine'i. Şu an yalnızca iskelet: sağlık uçları var,
// akış parça parça ekleniyor.
const string ServiceName = "hiwallet-withdrawal-orchestrator";

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Readiness'a henüz bağımlılık kaydedilmedi; kayıt olmadan uç 200 dönüyor ve
// bağımlılıklar geldikçe (Postgres, RabbitMQ) buraya ekleniyor.
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHiWalletHealthChecks();

app.Run();

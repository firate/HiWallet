using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Ön API, public: bireysel müşterinin mobil uygulaması. Veritabanına BAĞLANMIYOR;
// ledger'a giden her istek iç ağdaki wallet-api'den geçiyor.
const string ServiceName = "hiwallet-personal-mobile-api";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Readiness listesi boş: host'un tek bağımlılığı wallet-api ve onu çağıran istemci
// uçlarla birlikte geliyor.
builder.Services.AddHealthChecks();
builder.Services.AddHiWalletProblemDetails();
builder.Services.AddControllers();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();
app.MapHiWalletHealthChecks();
app.MapControllers();

// Integration testler WebApplicationFactory<PersonalMobileApiApp> ile ayağa kaldırır;
// gerekçe PersonalMobileApiApp.cs'te.
app.Run();

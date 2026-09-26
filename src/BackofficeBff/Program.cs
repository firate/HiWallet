using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Ön API, iç ağ: backoffice panelinin BFF'i. Tek istemcisi tarayıcıdaki backoffice
// paneli. Token tarayıcıya verilmez: bu host saklar, tarayıcı yalnızca HttpOnly oturum
// cookie'si taşır. Oturum akışı henüz kurulmadı. Çalışanın başlattığı işlemler
// (Actor.Employee, madde 34) buradan giriyor. Canlıda yalnızca iç ağdan erişiliyor;
// kısıtı ağ katmanı uyguluyor (madde 28). Veritabanına BAĞLANMIYOR; ledger'a giden her
// istek wallet-api'den geçiyor.
const string ServiceName = "hiwallet-backoffice-bff";

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

// Integration testler WebApplicationFactory<BackofficeBffApp> ile ayağa kaldırır;
// gerekçe BackofficeBffApp.cs'te.
app.Run();

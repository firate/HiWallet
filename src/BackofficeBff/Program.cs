using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using HiWallet.EdgeApi.Sessions;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Ön API, iç ağ: backoffice panelinin BFF'i. Tek istemcisi tarayıcıdaki backoffice
// paneli; giriş çalışanların realm'inden. Tarayıcı yalnızca oturum cookie'si taşıyor,
// token'ları görmüyor. Çalışanın başlattığı işlemler (Actor.Employee, madde 34) buradan
// giriyor. Canlıda yalnızca iç ağdan erişiliyor; kısıtı ağ katmanı uyguluyor (madde 28).
// Veritabanına BAĞLANMIYOR; ledger'a giden her istek wallet-api'den geçiyor.
const string ServiceName = "hiwallet-backoffice-bff";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Readiness listesi BOŞ: iç servisler ve Keycloak burada kontrol edilmiyor (mobil ön
// API'deki gerekçe).
builder.Services.AddHealthChecks();
builder.Services.AddHiWalletProblemDetails();

// Varsayılan politika oturum istiyor; izin istemiyor. Çalışanın izinleri token'da değil,
// personel yönetiminde: her isteğin iznini iç servis o anki haliyle kontrol ediyor.
builder.Services.AddBffSession(applicationName: ServiceName);

builder.Services.AddWalletApiClient();
builder.Services.AddWithdrawalOrchestratorClient();
builder.Services.AddStaffAdminClient();

// Çalışan başına kova. Panel müşteri uygulamasından daha sık istek atıyor (arama,
// liste); çekim kovası burada kullanılmıyor.
builder.Services.AddEdgeRateLimiting(
    client: new BucketDefaults(BurstSize: 50, SustainedPerMinute: 300),
    withdrawals: new BucketDefaults(BurstSize: 10, SustainedPerMinute: 30));
builder.Services.AddControllers();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Panelin dosyaları (wwwroot) kimliksiz: giriş sayfası da onlardan açılıyor. Kök adres
// index.html'e çevriliyor; aşağıdaki yedek yol boş yolu eşleştirmiyor.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseCsrfHeader("/v1");
app.UseRateLimiter();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();
app.MapHiWalletHealthChecks();
app.MapControllers();

// Panelin kendi yolları sayfayı alıyor; API ve oturum yolları hariç.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!v1/|bff/).*$)}", "index.html").AllowAnonymous();

// Integration testler WebApplicationFactory<BackofficeBffApp> ile ayağa kaldırır;
// gerekçe BackofficeBffApp.cs'te.
app.Run();

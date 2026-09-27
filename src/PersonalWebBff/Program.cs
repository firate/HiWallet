using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using HiWallet.EdgeApi.Sessions;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Ön API, public: bireysel müşterinin web uygulamasının BFF'i. Uygulamanın sayfalarını
// da bu host sunuyor; tarayıcı yalnızca oturum cookie'si taşıyor, token'ları görmüyor.
// Veritabanına BAĞLANMIYOR; ledger'a giden her istek iç ağdaki wallet-api'den geçiyor.
const string ServiceName = "hiwallet-personal-web-bff";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Readiness listesi BOŞ: iç servisler ve Keycloak burada kontrol edilmiyor (mobil ön
// API'deki gerekçe).
builder.Services.AddHealthChecks();
builder.Services.AddHiWalletProblemDetails();

builder.Services.AddBffSession(applicationName: ServiceName);

// İç servis istemcileri. Adresleri eksikse uygulama açılmıyor.
builder.Services.AddWalletApiClient();
builder.Services.AddWithdrawalOrchestratorClient();

// Müşterinin kovası mobil uygulamanınkiyle aynı: aynı müşteri, aynı işlemler.
builder.Services.AddEdgeRateLimiting(
    client: new BucketDefaults(BurstSize: 20, SustainedPerMinute: 60),
    withdrawals: new BucketDefaults(BurstSize: 10, SustainedPerMinute: 30));
builder.Services.AddControllers();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Uygulamanın dosyaları (wwwroot) kimliksiz: giriş sayfası da onlardan açılıyor.
// Veri API'den geliyor ve API oturum istiyor. Kök adres index.html'e çevriliyor; aşağıdaki
// yedek yol boş yolu eşleştirmiyor ve kök adres varsayılan politikaya düşüp 401 alıyordu.
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

// Uygulamanın kendi yolları (/cuzdanlar/...) sayfayı alıyor; yönlendirmeyi tarayıcıdaki
// uygulama yapıyor. API ve oturum yolları hariç: yanlış yazılmış bir uç 404 almalı,
// 200 ve HTML değil.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!v1/|bff/).*$)}", "index.html").AllowAnonymous();

// Integration testler WebApplicationFactory<PersonalWebBffApp> ile ayağa kaldırır;
// gerekçe PersonalWebBffApp.cs'te.
app.Run();

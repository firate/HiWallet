using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using HiWallet.Shared.Infrastructure.Authentication;
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

// Readiness listesi BOŞ: iç servisler burada kontrol edilmiyor. wallet-api düştüğünde
// ön API trafikten çekilseydi istemci yük dengeleyicinin gövdesiz hatasını alırdı;
// açık kalınca her istek ProblemDetails'li 503 dönüyor ve wallet-api dönünce kendiliğinden
// düzeliyor.
builder.Services.AddHealthChecks();
builder.Services.AddHiWalletProblemDetails();

// Müşterinin token'ı burada doğrulanıyor ve iç servislere aynen iletiliyor; iç
// servisler onu yeniden doğruluyor.
builder.Services.AddHiWalletAuthentication();

// İç servis istemcileri. Adresleri eksikse uygulama açılmıyor.
builder.Services.AddWalletApiClient();
builder.Services.AddWithdrawalOrchestratorClient();

// Müşterinin kovası: anlık 20, dakikada 60. Çekim başlatma ayrı kovada, 10 ve 30.
builder.Services.AddEdgeRateLimiting(
    client: new BucketDefaults(BurstSize: 20, SustainedPerMinute: 60),
    withdrawals: new BucketDefaults(BurstSize: 10, SustainedPerMinute: 30));
builder.Services.AddControllers();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Sıra: kimlik okunuyor, sonra kova seçiliyor, sonra kimlik zorunluluğu. Limiter
// yetkilendirmeden önce: geçersiz token'la gelen istek de sınırlanıyor.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();

// Health check'ler limitin DIŞINDA: probe'un limite takılması sağlıklı bir servisi
// trafikten çektirir. Kovayı her controller kendi attribute'uyla seçiyor; çekim
// başlatmanın kovası ayrı.
app.MapHiWalletHealthChecks();
app.MapControllers();

// Integration testler WebApplicationFactory<PersonalMobileApiApp> ile ayağa kaldırır;
// gerekçe PersonalMobileApiApp.cs'te.
app.Run();

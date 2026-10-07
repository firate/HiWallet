using HiWallet.EdgeApi.InternalServices;
using HiWallet.EdgeApi.RateLimiting;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Ön API, public: işyerinin sistem entegrasyonu. Çağıran işyerinin kendi sunucusu ve
// her istekte token taşıyor; oturum ve cookie YOK. Tarayıcıdaki panel business-web-bff'e
// bağlanıyor. Veritabanına BAĞLANMIYOR; ledger'a giden her istek iç ağdaki wallet-api'den
// geçiyor.
const string ServiceName = "hiwallet-business-api";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Readiness listesi BOŞ: iç servisler burada kontrol edilmiyor. wallet-api düştüğünde
// ön API trafikten çekilseydi istemci yük dengeleyicinin gövdesiz hatasını alırdı;
// açık kalınca her istek ProblemDetails'li 503 dönüyor.
builder.Services.AddHealthChecks();
builder.Services.AddHiWalletProblemDetails();

// Entegrasyon istemcisinin token'ı (client credentials) burada doğrulanıyor ve iç
// servislere aynen iletiliyor. Token'daki kimlik istemcinin servis hesabı; işyeri
// hesabının kullanıcısı olarak wallet'ta kayıtlı.
builder.Services.AddHiWalletAuthentication();

// İç servis istemcileri. Adresleri eksikse uygulama açılmıyor.
builder.Services.AddWalletApiClient();
builder.Services.AddWithdrawalOrchestratorClient();

// Kova entegrasyon istemcisi başına. İşyerinin sunucusu insandan hızlı çağırıyor:
// anlık 100, dakikada 600. Çekim başlatma ayrı kovada, 10 ve 30.
builder.Services.AddEdgeRateLimiting(
    client: new BucketDefaults(BurstSize: 100, SustainedPerMinute: 600),
    withdrawals: new BucketDefaults(BurstSize: 10, SustainedPerMinute: 30));

builder.Services.AddControllers();
builder.Services.AddHiWalletOpenApi(TokenFlows.ClientCredentials);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Sıra: kimlik okunuyor, sonra kova seçiliyor, sonra kimlik zorunluluğu.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();

// Health check'ler limitin DIŞINDA. Kovayı her controller kendi attribute'uyla seçiyor.
app.MapHiWalletHealthChecks();
app.MapControllers();

// Integration testler WebApplicationFactory<BusinessApiApp> ile ayağa kaldırır;
// gerekçe BusinessApiApp.cs'te.
app.Run();

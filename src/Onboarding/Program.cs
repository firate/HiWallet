using System.Text.Json.Serialization;
using HiWallet.Onboarding.Setup;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// İç servis: müşteri kaydı ve kimlik doğrulaması. Ön API'ler çağırıyor; kayıt uçları
// kimliksiz, doğrulama uçları müşterinin token'ıyla. Kişisel veri kendi veritabanında,
// ledger'dan ayrı. Wallet'a yalnızca "hesabı aç" ve "seviyeyi yükselt" gidiyor.
const string ServiceName = "hiwallet-onboarding";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddOnboarding();
builder.Services.AddOnboardingValidation();

// Token'ı ön API iletiyor, burada yeniden doğrulanıyor.
builder.Services.AddHiWalletAuthentication();
builder.Services.AddWalletConsumerAccess(builder.Configuration);

builder.Services
    .AddControllers(options => options.Filters.AddService<ValidationFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHiWalletProblemDetails();
builder.Services.AddExceptionHandler<OnboardingExceptionHandler>();
builder.Services.AddHiWalletOpenApi(TokenFlows.AuthorizationCode);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();
app.MapHiWalletHealthChecks();

// Rate limit YOK: iç servis, sınır ön API'de.
app.MapControllers();

// Integration testler WebApplicationFactory<OnboardingApp> ile ayağa kaldırır.
app.Run();

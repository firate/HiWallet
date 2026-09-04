using FluentValidation;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.TopupWebhook.Api.Validators;
using HiWallet.TopupWebhook.Application;
using HiWallet.TopupWebhook.Infrastructure.Messaging;
using HiWallet.TopupWebhook.Setup;
using Microsoft.AspNetCore.RateLimiting;

const string ServiceName = "hiwallet-topup-webhook";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10). Relay bir batch'i
// yayınlamış ama commit etmemişken kesilirse mesajlar iki kez gider; süre tanımak
// bunu nadirleştiriyor (tüketici zaten deduplike ediyor).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddTopupPersistence();
builder.Services.AddWebhookSecrets();
builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddTopupHealthChecks();
builder.Services.AddTopupRateLimiting(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TopupInboxWriter>();
builder.Services.AddValidatorsFromAssemblyContaining<TopupWebhookPayloadValidator>();

// Relay webhook servisinin İÇİNDE, ayrı bir uygulama değil: inbox tablosunun sahibi
// bu servis ve relay o tablodan başka hiçbir şeye bakmıyor. Ayrı süreç olsaydı aynı
// tabloya ikinci bir yazar eklenirdi, karşılığında hiçbir şey kazanılmadan.
builder.Services.AddHostedService<TopupRelay>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.ValidateTopupConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// Health check'lere rate limit UYGULANMIYOR: probe'un limite takılması sağlıklı bir
// servisi trafikten çektirir.
app.MapHiWalletHealthChecks();

app.MapControllers().RequireRateLimiting(RateLimitingSetup.WebhookPolicy);

app.Run();

// Integration testler WebApplicationFactory<Program> ile ayağa kaldırır.
public partial class Program;

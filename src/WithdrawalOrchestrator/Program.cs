using FluentValidation;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.WithdrawalOrchestrator.Api.Validators;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Messaging;
using HiWallet.WithdrawalOrchestrator.Setup;
using HiWallet.Shared.Infrastructure.Errors;

// Withdrawal saga'sının state machine'i. Komutları wallet-consumer ve bank-adapter
// tüketiyor; bu servis saga'yı onların cevaplarıyla ilerletiyor.
const string ServiceName = "hiwallet-withdrawal-orchestrator";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddOrchestratorPersistence();
builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddOrchestratorHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StartWithdrawalHandler>();
builder.Services.AddScoped<AdvanceSagaHandler>();
builder.Services.AddScoped<ReviewWithdrawalHandler>();

// İnceleme eşiği. Bölüm eksikse uygulama açılmıyor: sessizce incelemesiz çalışmamalı.
builder.Services.AddOptions<WithdrawalReviewOptions>()
    .BindConfiguration(WithdrawalReviewOptions.SectionName)
    .Validate(options => options.Above.Count > 0,
        $"{WithdrawalReviewOptions.SectionName}:Above boş; hangi çekimin incelemeye gireceği bilinmiyor.")
    .Validate(options => options.Above.Values.All(threshold => threshold > 0m),
        $"{WithdrawalReviewOptions.SectionName}:Above değerleri pozitif olmalı.")
    .ValidateOnStart();
builder.Services.AddScoped<WithdrawalQueries>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateWithdrawalRequestValidator>();

// Relay orchestrator'ın İÇİNDE, ayrı bir uygulama değil: outbox tablosunun sahibi
// bu servis ve relay o tablodan başka hiçbir şeye bakmıyor. Ayrı süreç olsaydı aynı
// tabloya ikinci bir yazar eklenirdi, karşılığında hiçbir şey kazanılmadan.
builder.Services.AddHostedService<WithdrawalOutboxRelay>();
builder.Services.AddHostedService<WithdrawalEventConsumer>();

// Takılmış saga taraması. Ayrı veritabanı kararının (decisions.md madde 7 ve 33)
// zorunlu tamamlayıcısı: iki veritabanı arasında ayrışma olduğunda asılı kalmış
// çekimi yakalayacak başka hiçbir mekanizma yok.
builder.Services.Configure<StuckSagaScanOptions>(
    builder.Configuration.GetSection(StuckSagaScanOptions.SectionName));
builder.Services.AddHiWalletJobLease(PersistenceSetup.ConnectionStringName);
builder.Services.AddSingleton<StuckSagaScanner>();
builder.Services.AddHostedService<StuckSagaScan>();

// Token'ı ön API iletiyor, burada yeniden doğrulanıyor. Çalışanların realm'inin
// token'ı da kabul ediliyor; çalışan yalnızca izin veren uçtan geçiyor.
builder.Services.AddHiWalletAuthentication(acceptStaffTokens: true);
builder.Services.AddControllers();
builder.Services.AddHiWalletProblemDetails();
builder.Services.AddHiWalletOpenApi(TokenFlows.AuthorizationCode | TokenFlows.ClientCredentials);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();

app.MapHiWalletHealthChecks();

// Rate limit YOK: iç servis, müşteri başına sınır ön API'de.
app.MapControllers();

// Integration testler WebApplicationFactory<WithdrawalOrchestratorApp> ile ayağa
// kaldırır; gerekçe WithdrawalOrchestratorApp.cs'te.
app.Run();

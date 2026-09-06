using FluentValidation;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Jobs;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.WithdrawalOrchestrator.Api.Validators;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Messaging;
using HiWallet.WithdrawalOrchestrator.Setup;

// Withdrawal saga'sının state machine'i. Karşı taraf (wallet komut handler'ları ve
// bank-service) henüz yok; bu servis kendi tarafını baştan sona yürütüyor.
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

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();
app.MapControllers();

// Integration testler WebApplicationFactory<WithdrawalOrchestratorApp> ile ayağa
// kaldırır; gerekçe WithdrawalOrchestratorApp.cs'te.
app.Run();

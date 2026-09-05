using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.WithdrawalOrchestrator.Setup;

// Withdrawal saga'sının state machine'i. Akış parça parça ekleniyor; şu an
// kalıcılık ve sağlık uçları var, API ile mesajlaşma henüz yok.
const string ServiceName = "hiwallet-withdrawal-orchestrator";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddOrchestratorPersistence();
builder.Services.AddOrchestratorHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapHiWalletHealthChecks();

// Integration testler WebApplicationFactory<WithdrawalOrchestratorApp> ile ayağa
// kaldırır; gerekçe WithdrawalOrchestratorApp.cs'te.
app.Run();

using FluentValidation;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.WithdrawalOrchestrator.Api.Validators;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Setup;

// Withdrawal saga'sının state machine'i. Akış parça parça ekleniyor; şu an kalıcılık
// ve API var, broker tarafı (outbox relay, event tüketicisi) henüz yok.
const string ServiceName = "hiwallet-withdrawal-orchestrator";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddOrchestratorPersistence();
builder.Services.AddOrchestratorHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<StartWithdrawalHandler>();
builder.Services.AddScoped<WithdrawalQueries>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateWithdrawalRequestValidator>();

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

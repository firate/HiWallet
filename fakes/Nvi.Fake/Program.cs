using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// NÜFUS KAYDININ YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35). Canlıda YOK.
// Biçimi geçerli her numara eşleşiyor; eşleşmemesi istenen numara POST /v1/scenarios ile.
const string ServiceName = "hiwallet-nvi-fake";

var builder = WebApplication.CreateBuilder(args);

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<ConcurrentDictionary<string, HiWallet.Nvi.Fake.IdentityOutcome>>();
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();
app.MapControllers();

app.Run();

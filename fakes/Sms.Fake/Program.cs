using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.Sms.Fake;

// SMS SAĞLAYICISININ YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35). Canlıda YOK.
// Gönderilen mesajı kutusunda tutuyor; deneyen kişi kodu GET /v1/messages'tan okuyor.
const string ServiceName = "hiwallet-sms-fake";

var builder = WebApplication.CreateBuilder(args);

builder.AddHiWalletObservability(ServiceName);

// Veritabanı yok: hazır olmak process'in ayakta olması.
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SmsInbox>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();
app.MapControllers();

app.Run();

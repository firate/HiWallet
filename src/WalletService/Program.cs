using System.Text.Json.Serialization;
using HiWallet.WalletService.Setup;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için verilen süre (baseline.md madde 10).
// Varsayılan 5 sn; bir transfer'in DB turu buna sığar ama yük altında sıkışık.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability();

// Wolverine YALNIZCA in-process mediator olarak: RabbitMQ transport'u, durable
// inbox/outbox'ı ve Saga persistence'ı kullanılmıyor (decisions.md madde 1).
builder.Host.UseWolverine();

builder.Services.AddHiWalletPersistence();
builder.Services.AddHiWalletPolicies(builder.Configuration);
builder.Services.AddHiWalletValidation();
builder.Services.AddHiWalletProblemDetails();
builder.Services.AddHiWalletHealthChecks(builder.Configuration);
builder.Services.AddHiWalletRateLimiting(builder.Configuration);

builder.Services
    .AddControllers(options => options.Filters.AddService<ValidationFilter>())
    .AddJsonOptions(options =>
        // Enum'lar sözleşmede İSİM olarak geçer: "P2P", sayı değil. Sayı olsaydı
        // enum'a yeni bir değer eklemek mevcut client'ların anlamını kaydırırdı.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.ValidateHiWalletConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check'lere rate limit UYGULANMIYOR: probe'un limite takılması sağlıklı bir
// servisi trafikten çektirir.
app.MapHiWalletHealthChecks();

app.MapControllers().RequireRateLimiting(RateLimitingSetup.TransfersPolicy);

app.Run();

// Integration testler WebApplicationFactory<Program> ile ayağa kaldırır.
public partial class Program;

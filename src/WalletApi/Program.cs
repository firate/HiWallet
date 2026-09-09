using System.Text.Json.Serialization;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.WalletApi.Setup;
using HiWallet.WalletService.Setup;
using Microsoft.AspNetCore.RateLimiting;
using Wolverine;

// Public ingress. Mobil ve web istemciler buraya bağlanıyor; banka webhook'ları
// BURAYA GELMİYOR — onlar IP kısıtlı topup-webhook'ta (decisions.md madde 28).
const string ServiceName = "hiwallet-wallet-api";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için verilen süre (baseline.md madde 10).
// Varsayılan 5 sn; bir transfer'in DB turu buna sığar ama yük altında sıkışık.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

// Wolverine YALNIZCA in-process mediator olarak: RabbitMQ transport'u, durable
// inbox/outbox'ı ve Saga persistence'ı kullanılmıyor (decisions.md madde 1).
builder.Host.UseWolverine(options =>
    // Handler'lar WalletService.Core'da; Wolverine varsayılan olarak yalnızca
    // giriş assembly'sini tarıyor. Bu satır olmadan dispatch çalışma anında
    // IndeterminateRoutesException veriyor — derleme hatası DEĞİL, o yüzden
    // ayrıştırmadan sonra ilk testte ortaya çıktı.
    options.Discovery.IncludeAssembly(typeof(HiWallet.WalletService.Application.Transfers.CreateTransferHandler).Assembly));

// AddHiWalletMessaging ÇAĞRILMIYOR: bu uygulamanın broker'a hiç işi yok. Top-up
// tüketicisi ayrı bir host'a taşındıktan sonra burada tek bağımlılık Postgres kaldı.
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

builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.ValidateHiWalletConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// Development kapısı MapHiWalletOpenApi'nin içinde; üretimde iki uç da yok.
app.MapHiWalletOpenApi();

// Health check'lere rate limit UYGULANMIYOR: probe'un limite takılması sağlıklı bir
// servisi trafikten çektirir.
app.MapHiWalletHealthChecks();

app.MapControllers().RequireRateLimiting(RateLimitingSetup.TransfersPolicy);

// Integration testler WebApplicationFactory<WalletApiApp> ile ayağa kaldırır;
// gerekçe WalletApiApp.cs'te.
app.Run();

using System.Text.Json.Serialization;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.WalletApi.Setup;
using HiWallet.WalletService.Application.Accounts;
using HiWallet.WalletService.Application.Promos;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Setup;
using Wolverine;

// İç servis. İstemci buraya doğrudan bağlanmıyor; ön API'ler çağırıyor. Banka
// webhook'ları da BURAYA GELMİYOR — onlar IP kısıtlı topup-webhook'ta (decisions.md madde 28).
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

// Seviyenin transfer ve ödeme limitleri yalnızca burada; çekim limiti wallet-consumer'da.
builder.Services.AddKycLimits(
    builder.Configuration, KycMovement.IncomingTransfer, KycMovement.OutgoingTransfer, KycMovement.Payment);
builder.Services.AddHiWalletValidation();
builder.Services.AddWalletProblemDetails();
builder.Services.AddHiWalletHealthChecks(builder.Configuration);

// Token'ı ön API iletiyor, burada yeniden doğrulanıyor: ön API'ye körü körüne
// güvenilmiyor. Sahiplik kontrolü de burada, ledger'ın sahibinde. Çalışanların
// realm'inin token'ı da kabul ediliyor; çalışan yalnızca izin veren uçtan geçiyor.
builder.Services.AddHiWalletAuthentication(acceptStaffTokens: true);
builder.Services.AddOnboardingAccess(builder.Configuration);
builder.Services.AddSingleton<AccountAccess>();

// Personel promo'sunun tek seferlik tavanı. Tanımlı olmayan para biriminde personel
// promo'su verilmiyor.
builder.Services.AddOptions<StaffPromoOptions>()
    .BindConfiguration(StaffPromoOptions.SectionName)
    .Validate(options => options.MaxAmount.Values.All(max => max > 0m),
        $"{StaffPromoOptions.SectionName}:MaxAmount değerleri pozitif olmalı.")
    .ValidateOnStart();

builder.Services
    .AddControllers(options => options.Filters.AddService<ValidationFilter>())
    .AddJsonOptions(options =>
        // Enum'lar sözleşmede İSİM olarak geçer: "P2P", sayı değil. Sayı olsaydı
        // enum'a yeni bir değer eklemek mevcut client'ların anlamını kaydırırdı.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHiWalletOpenApi(TokenFlows.AuthorizationCode | TokenFlows.ClientCredentials);

var app = builder.Build();

app.ValidateHiWalletConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();

app.MapHiWalletHealthChecks();

// Rate limit YOK: müşteri başına sınır ön API'de. Buraya gelen trafiğin kaynağı ön
// API'ler; IP'ye göre bölünen bir kova bütün müşterileri tek kovaya koyardı.
app.MapControllers();

// Integration testler WebApplicationFactory<WalletApiApp> ile ayağa kaldırır;
// gerekçe WalletApiApp.cs'te.
app.Run();

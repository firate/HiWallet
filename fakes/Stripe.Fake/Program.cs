using FluentValidation;
using HiWallet.Fakes.Topups;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// KART SAĞLAYICISININ YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35).
//
// Üretimde YOK. Yalnızca PARA GİRİŞİ yapıyor: komutla topup-webhook'a imzalı
// webhook gönderiyor. Stripe'tan para çıkmadığı için transfer ucu da yok,
// callback alıcısı da.
//
// VERİTABANI YOK: tetikleyen taraf zaten bir test ya da insan, süreç ölürse
// yeniden tetikliyor.
const string ServiceName = "hiwallet-stripe-fake";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken kuyruktaki gönderim bitsin. Bitmezse kaybolur — kabul edilebilir,
// çünkü yeniden tetiklenebilir.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddFakeTopupProvider(builder.Configuration, provider: "stripe-fake");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<TopupRequestValidator>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Postgres kontrolü YOK: bu servisin veritabanı yok. Sağlıklı olması yalnızca
// process'in ayakta olması demek ve başka bir şey iddia etmiyor.
app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();

app.MapControllers();

app.Run();

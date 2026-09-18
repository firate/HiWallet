using System.Text.Json.Serialization;
using FluentValidation;
using HiWallet.Bank.Fake.Api.Requests;
using HiWallet.Bank.Fake.Setup;
using HiWallet.Fakes.Topups;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// BANKANIN YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35).
//
// Canlıda YOK: yerine gerçek bankanın kendi endpoint'i geçiyor ve bu proje siliniyor.
// Bizim tarafımız `bank-adapter`; ayrımın ölçütü "test amaçlı mı" değil, "başka bir
// kurumun yerine mi duruyor".
//
// SINIRI GERÇEK TUTAN ŞEYLER: RabbitMQ'ya hiç bağlanmıyor (gerçek banka müşterisinin
// broker'ını dinlemez), wallet'ı ve orchestrator'ı görmüyor, Shared.Contracts'a
// referansı yok ve hafızası kendi process'inde.
//
// VERİTABANI YOK: transferler ve senaryolar bellekte, yeniden başlatınca siliniyor
// (decisions.md madde 35). Elle ve testle denemek için var; geçmiş saklamıyor.
const string ServiceName = "hiwallet-bank-fake";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken gönderilmekte olan callback bitsin. Bekleyen callback'ler bellekle
// birlikte kayboluyor; o çekimleri adaptörün mutabakat taraması da kapatamaz,
// çünkü yeniden başlayan banka transferi tanımıyor.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddBankFake(builder.Configuration);

// PARA GİRİŞİ tarafı: banka havale bildirimini topup-webhook'a gönderiyor.
// Sağlayıcı kimliği SABİT — bir sahte servis tek bir kurumu temsil eder ve
// çalışma anında kimliğini değiştiremez.
builder.Services.AddFakeTopupProvider(builder.Configuration, provider: "bank-fake");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<ScenarioRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<TopupRequestValidator>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        // Enum'lar İSİM olarak geçer ("Failure", "Duplicate"), wallet-api'deki gibi.
        // Bu ayar yokken string gönderen her request 400 alıyordu — senaryo ve
        // tetikleme endpoint'lerinin tamamı, elle ya da testten, hiç çalışmıyordu.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();

app.MapControllers();

// Integration testler WebApplicationFactory<BankFakeApp> ile ayağa kaldırır;
// gerekçe BankFakeApp.cs'te.
app.Run();

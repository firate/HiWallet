using FluentValidation;
using HiWallet.Bank.Fake.Api.Requests;
using HiWallet.Bank.Fake.Setup;
using HiWallet.Fakes.Topups;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// BANKANIN YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35).
//
// Üretimde YOK: yerine gerçek bankanın kendi ucu geçiyor ve bu proje siliniyor.
// Bizim tarafımız `bank-adapter`; ayrımın ölçütü "test amaçlı mı" değil, "başka bir
// kurumun yerine mi duruyor".
//
// SINIRI GERÇEK TUTAN ŞEYLER: RabbitMQ'ya hiç bağlanmıyor (gerçek banka müşterisinin
// broker'ını dinlemez), wallet'ı ve orchestrator'ı görmüyor, Shared.Contracts'a
// referansı yok ve kendi veritabanında duruyor.
const string ServiceName = "hiwallet-bank-fake";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken gönderilmekte olan callback bitsin. Bitmezse satır "gönderilmedi"
// kalıyor ve bir sonraki turda yeniden deneniyor — kayıp yok, yalnızca gecikme.
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

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.ValidateBankFakeConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();

app.MapControllers();

// Integration testler WebApplicationFactory<BankFakeApp> ile ayağa kaldırır;
// gerekçe BankFakeApp.cs'te.
app.Run();

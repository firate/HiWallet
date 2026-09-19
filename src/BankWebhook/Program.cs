using HiWallet.BankWebhook.Setup;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// BANKANIN BİZİ ÇAĞIRDIĞI UÇ — bizim kodumuz, canlıda da koşuyor
// (decisions.md madde 35).
//
// AYRI DEPLOYABLE ÇÜNKÜ INGRESS'İ VAR. `bank-adapter`'ın hiç ingress'i yok;
// farklı erişim seviyesi aynı process'te birleşmiyor (madde 28). Dağıtım tarafında da
// karşılığı var: tarama ya da yayın mantığı değişince bankanın çağırdığı endpoint
// yeniden başlatılmıyor.
//
// TEK İŞİ: doğrula, inbox'a yaz, 202. Broker'a hiç bağlanmıyor.
const string ServiceName = "hiwallet-bank-webhook";

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddBankWebhook();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.ValidateBankWebhookConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();

app.MapControllers();

app.Run();

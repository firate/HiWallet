using FluentValidation;
using HiWallet.BankService.Api.Requests;
using HiWallet.BankService.Setup;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;

// Gerçek bankanın yerine geçen sahte servis (overview.md madde 9). Varlık sebebi
// test edilebilirlik: "banka reddetti" senaryosu gerçek bir bankayla üretilemez,
// oysa telafi yolunun çalıştığını kanıtlamanın tek yolu onu tetikleyebilmek.
//
// Wallet sınırını GÖRMÜYOR: ledger'dan, komisyondan, saga durumundan haberi yok.
const string ServiceName = "hiwallet-bank-service";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken işlenmekte olan komut bitsin. Bitmezse ack'lenmez ve yeniden teslim
// edilir; transfer kaydı idempotent olduğu için para iki kez gitmiyor.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddBankService();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<ArmScenarioRequestValidator>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.ValidateBankServiceConfiguration();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();

// Tek controller: senaryo tetikleyicileri. Gerçek bir bankada bu uç yoktur.
app.MapControllers();

// Integration testler WebApplicationFactory<BankServiceApp> ile ayağa kaldırır;
// gerekçe BankServiceApp.cs'te.
app.Run();

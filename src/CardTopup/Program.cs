using FluentValidation;
using HiWallet.CardTopup.Api.Validators;
using HiWallet.CardTopup.Setup;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;

// Kartla yüklemenin ömrü: limit payını wallet-api'den ister, ödemeyi sağlayıcıda açar,
// sonucunu bildirimden ya da sağlayıcıya sorarak öğrenir ve kapanışı wallet'a gönderir.
const string ServiceName = "hiwallet-card-topup";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHiWalletMessaging(builder.Configuration, ServiceName);
builder.Services.AddCardTopup();
builder.Services.AddValidatorsFromAssemblyContaining<StartCardTopupRequestValidator>();

// Token'ı ön API iletiyor, burada yeniden doğrulanıyor ve wallet-api'ye aynen gidiyor.
// Çalışanın token'ı yalnızca okuma ucundan geçiyor.
builder.Services.AddHiWalletAuthentication(acceptStaffTokens: true);
builder.Services.AddControllers();
builder.Services.AddHiWalletProblemDetails();
builder.Services.AddHiWalletOpenApi(TokenFlows.AuthorizationCode);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHiWalletOpenApi();
app.MapHiWalletHealthChecks();

// Rate limit YOK: iç servis, müşteri başına sınır ön API'de.
app.MapControllers();

app.Run();

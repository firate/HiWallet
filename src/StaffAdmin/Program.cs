using System.Text.Json.Serialization;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.Shared.Infrastructure.Errors;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.StaffAdmin.Setup;

// İç servis: personel yönetimi. Çalışanlar, panelin rolleri, atamalar ve kim kime ne
// verdi kendi veritabanında; çalışanların Keycloak'ında yalnızca kullanıcıyı açıyor, davet
// ediyor ve kapatıyor. Çağıranlar backoffice-bff (panel) ile wallet-api ve orchestrator
// (çalışanın her isteğinde izni soruyorlar, v1/me). Müşterinin token'ını tanımıyor.
const string ServiceName = "hiwallet-staff-admin";

var builder = WebApplication.CreateBuilder(args);

// SIGTERM'de in-flight işlerin bitmesi için (baseline.md madde 10).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddStaffAdmin();
builder.Services.AddStaffAdminValidation();

// Token'ı backoffice-bff iletiyor, burada yeniden doğrulanıyor.
builder.Services.AddHiWalletStaffAuthentication();

builder.Services
    .AddControllers(options => options.Filters.AddService<ValidationFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHiWalletProblemDetails();
builder.Services.AddExceptionHandler<StaffAdminExceptionHandler>();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// Development kapısı MapHiWalletOpenApi'nin içinde; canlıda iki endpoint da yok.
app.MapHiWalletOpenApi();
app.MapHiWalletHealthChecks();

// Rate limit YOK: iç servis, sınır ön API'de.
app.MapControllers();

// Integration testler WebApplicationFactory<StaffAdminApp> ile ayağa kaldırır.
app.Run();

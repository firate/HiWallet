using System.Threading.Channels;
using FluentValidation;
using HiWallet.Shared.Infrastructure.HealthChecks;
using HiWallet.Shared.Infrastructure.Observability;
using HiWallet.Shared.Infrastructure.OpenApi;
using HiWallet.Stripe.Fake.Api.Controllers;
using HiWallet.Stripe.Fake.Payments;
using HiWallet.Stripe.Fake.Webhooks;
using Microsoft.Extensions.Options;

// KART SAĞLAYICISININ YERİNDE DURAN SAHTE SERVİS (decisions.md madde 35).
//
// Canlıda YOK. Yalnızca PARA GİRİŞİ: kart yüklemesi servisi burada ödeme açıyor, müşteri
// ödeme sayfasında kararını veriyor, sonuç topup-webhook'a imzalı webhook olarak gidiyor.
// Stripe'tan para çıkmadığı için transfer ucu da callback alıcısı da yok.
//
// VERİTABANI YOK: ödemeler bellekte, yeniden başlatınca siliniyor (bankanın sahtesindeki gibi).
const string ServiceName = "hiwallet-stripe-fake";

var builder = WebApplication.CreateBuilder(args);

// Kapanırken kuyruktaki webhook gönderilsin. Gönderilmezse kart yüklemesi servisinin
// taraması sonucu sağlayıcıya sorarak yine buluyor.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.AddHiWalletObservability(ServiceName);

builder.Services.AddOptions<StripeFakeOptions>()
    .BindConfiguration(StripeFakeOptions.SectionName)
    .Validate(o => Uri.TryCreate(o.WebhookUrl, UriKind.Absolute, out _),
        $"{StripeFakeOptions.SectionName}:WebhookUrl boş; webhook gönderilemez.")
    // İmzasız gönderen bir sağlayıcı doğrulayıcımızın hiç sınanmaması demek olurdu.
    .Validate(o => !string.IsNullOrWhiteSpace(o.WebhookSecret),
        $"{StripeFakeOptions.SectionName}:WebhookSecret boş; imzasız webhook 401 alır.")
    .Validate(o => Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out _),
        $"{StripeFakeOptions.SectionName}:PublicUrl boş; müşteri ödeme sayfasına yönlendirilemez.")
    .ValidateOnStart();

builder.Services.AddHttpClient(PaymentWebhookSender.HttpClientName, (provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<StripeFakeOptions>>().Value;

    client.BaseAddress = new Uri(options.WebhookUrl!.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddSingleton<PaymentWebhookSender>();

// Kuyruk sınırlı: dolduğunda yazan bekliyor; sahte servisin yavaşlaması sessizce bildirim
// düşürmesinden iyi.
builder.Services.AddSingleton(Channel.CreateBounded<PaymentWebhook>(
    new BoundedChannelOptions(capacity: 256) { FullMode = BoundedChannelFullMode.Wait }));
builder.Services.AddHostedService<PaymentWebhookWorker>();

builder.Services.AddValidatorsFromAssemblyContaining<OpenPaymentRequestValidator>();

// Kayıt ŞART, eklenecek kontrol olmasa bile: MapHiWalletHealthChecks servisleri istiyor.
// Veritabanı olmadığı için hazır olmak yalnızca process'in ayakta olması demek.
builder.Services.AddHealthChecks();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHiWalletOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHiWalletHealthChecks();
app.MapHiWalletOpenApi();

app.MapControllers();

app.Run();

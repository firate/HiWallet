using HiWallet.Bank.Fake.Application;
using HiWallet.Bank.Fake.Infrastructure.Callbacks;
using HiWallet.Bank.Fake.Infrastructure.Storage;

namespace HiWallet.Bank.Fake.Setup;

public static class BankFakeSetup
{
    public static IServiceCollection AddBankFake(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BankFakeOptions>(configuration.GetSection(BankFakeOptions.SectionName));

        // Bankanın bütün hafızası. SINGLETON olmak zorunda: scoped olsaydı her
        // istek boş bir bankayla karşılaşırdı.
        services.AddSingleton<BankFakeStore>();

        services.AddScoped<AcceptTransferHandler>();
        services.AddScoped<ScenarioStore>();
        services.AddScoped<TransferQueries>();

        // Callback göndericisi kendi HTTP istemcisini kullanıyor. Timeout kısa:
        // ulaşılamayan bir uca takılıp kalmak, sırada bekleyen callback'leri de
        // geciktirirdi.
        services.AddHttpClient(CallbackDispatcher.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        services.AddHostedService<CallbackDispatcher>();

        // Kayıt ŞART, eklenecek kontrol olmasa bile: MapHiWalletHealthChecks
        // servisleri istiyor. Postgres ve RabbitMQ kontrolü YOK — sahte banka
        // ikisine de bağlanmıyor. Hazır olmak process'in ayakta olması demek.
        services.AddHealthChecks();

        return services;
    }
}

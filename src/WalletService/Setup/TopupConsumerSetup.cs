using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Infrastructure.Messaging;

namespace HiWallet.WalletService.Setup;

public static class TopupConsumerSetup
{
    public static IServiceCollection AddHiWalletTopupConsumer(this IServiceCollection services)
    {
        services.AddScoped<ProcessTopupHandler>();
        services.AddHostedService<TopupConsumer>();

        return services;
    }
}

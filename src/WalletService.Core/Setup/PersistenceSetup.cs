using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Setup;

public static class PersistenceSetup
{
    /// <summary>
    /// Uygulamanın kullandığı bağlantı. Migration'ın kullandığı
    /// <c>ConnectionStrings:WalletOwner</c> DEĞİL — uygulama tablo sahibi olmamalı,
    /// yoksa <c>ledger_entries</c> üzerindeki REVOKE hiçbir şey yapmaz.
    /// </summary>
    public const string ConnectionStringName = "Wallet";

    public static IServiceCollection AddHiWalletPersistence(this IServiceCollection services)
    {
        // Bağlantı dizesi KAYIT anında değil, context kurulurken okunuyor. Eager okumak
        // testleri kırıyordu: WebApplicationFactory kendi konfigürasyonunu host
        // kurulduktan SONRA ekliyor, kayıt anında okunan değer onu hiç görmüyordu.
        // Fail-fast ConfigValidation'a taşındı.
        services.AddDbContextFactory<WalletDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            options.UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                // Retry'ı EF yapmıyor: çakışma yönetimi handler'da, kendi kuralımızla
                // (decisions.md madde 9). İkisi üst üste binerse aynı transfer birden
                // fazla kez denenmiş olur.
                npgsql => npgsql.EnableRetryOnFailure(0));
        });

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}

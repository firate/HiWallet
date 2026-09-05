using HiWallet.WalletApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Gerçek uygulamayı ayağa kaldırır, yalnızca bağlantı dizesini bu koşunun schema'sına
/// çevirir. Böylece controller, validation filtresi, Wolverine dispatch'i ve
/// ProblemDetails eşlemesi gerçekten çalışıyor mu görünür — handler'ı doğrudan
/// çağıran testler bu katmanların hiçbirini kapsamıyor.
/// </summary>
public sealed class WalletApiFactory(PostgresFixture postgres) : WebApplicationFactory<WalletApiApp>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Broker ayarı YOK: wallet-api'nin RabbitMQ ile hiç işi kalmadı,
            // tüketici ayrı host'a taşındı.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Wallet"] = postgres.ConnectionString
            });
        });

        // Sunucu tarafındaki istisnalar ProblemDetails'in arkasında kayboluyor;
        // test başarısız olduğunda sebebini görebilmek için yakalanıyor.
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Errors)));
    }

    public List<string> Errors { get; } = [];

    private sealed class CapturingLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(sink);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (sink)
            {
                sink.Add($"{formatter(state, exception)} :: {exception}");
            }
        }
    }
}

using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Tek bir AMQP bağlantısını paylaştırır. Bağlantı pahalı, kanal ucuz: uygulama
/// başına bir bağlantı açılır, iş başına kanal alınır.
///
/// Bağlantı uygulama başlarken DEĞİL, ilk ihtiyaç anında kuruluyor. Broker
/// uygulamadan sonra ayağa kalkarsa servis yine de başlasın diye — broker'a
/// bağlanamamak sağlık ucunda görünür, başlangıçta çökme sebebi değil.
/// </summary>
public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var settings = options.Value;

        _factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            ClientProvidedName = settings.ClientName,
            // Kopan bağlantıyı ve üstündeki topolojiyi istemci kendisi geri kuruyor.
            // Kapalıysa her publish/consume noktasına elle yeniden bağlanma kodu
            // yazmak gerekirdi.
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            // Varsayılan 30 sn. Broker kapalıyken her deneme yarım dakika asılı
            // kalıyor; sağlık kontrolü beklemeyi 2 sn'de bıraksa bile deneme arka
            // planda _gate'i o kadar süre tutuyordu. Kısa tutmanın bedeli yok:
            // bağlantı kurulamadığında yeniden denenecek zaten.
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };
    }

    public async ValueTask<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            // Beklerken başkası açmış olabilir.
            if (_connection is { IsOpen: true }) return _connection;

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _connection = await _factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}

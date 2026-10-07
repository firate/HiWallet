using System.Reflection;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

[assembly: AssemblyFixture(typeof(HiWallet.IntegrationTests.Fixtures.BrokerCleanup))]

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Koşunun broker'da açtığı exchange ve kuyrukları koşu bitince siler. Testler test
/// ortamının broker'ına bağlanıyor ve her koşu kendi ön ekiyle (<see cref="BrokerSettings.NamePrefix"/>)
/// topoloji kuruyor; Postgres'teki koşu şeması gibi bunun da sonunda gitmesi gerekiyor.
///
/// Adlar topoloji sınıflarından okunuyor, elle listelenmiyor: yeni bir topoloji eklendiğinde
/// temizlik listesi unutulmasın. Kural: <c>Shared.Infrastructure.Messaging</c>'de adı
/// <c>Topology</c> ile biten sınıf; adı <c>Queue</c> ile biten özellik kuyruk, <c>Exchange</c>
/// ile biten exchange.
///
/// Süreç çökerse temizlik koşmuyor ve o koşunun artığı broker'da kalıyor.
/// </summary>
public sealed class BrokerCleanup : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!BrokerSettings.Configured) return;

        try
        {
            await DeleteAsync(BrokerSettings.BuildOptions("hiwallet-tests-cleanup"), CancellationToken.None);
        }
        catch (Exception exception) when (exception is BrokerUnreachableException or OperationInterruptedException)
        {
            // Broker'a ulaşılamıyorsa bu koşu orada bir şey de açamadı.
        }
    }

    /// <summary>Verilen ön ekle bütün topolojilerin kuyruk ve exchange adları.</summary>
    public static IReadOnlyList<TopologyNames> Topologies(RabbitMqOptions options)
    {
        var wrapped = Options.Create(options);

        return typeof(RabbitMqOptions).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace == typeof(RabbitMqOptions).Namespace
                           && type.Name.EndsWith("Topology", StringComparison.Ordinal))
            .Select(type => TopologyNames.Of(Activator.CreateInstance(type, wrapped)!))
            .ToList();
    }

    public static async Task DeleteAsync(RabbitMqOptions options, CancellationToken ct)
    {
        await using var connection = new RabbitMqConnection(Options.Create(options));

        foreach (var topology in Topologies(options))
        {
            foreach (var queue in topology.Queues)
            {
                await DeleteAsync(connection, channel => channel.QueueDeleteAsync(queue, cancellationToken: ct), ct);
            }

            foreach (var exchange in topology.Exchanges)
            {
                await DeleteAsync(connection, channel => channel.ExchangeDeleteAsync(exchange, cancellationToken: ct), ct);
            }
        }
    }

    // Hiç açılmamış bir ad broker'ın sürümüne göre hata dönebiliyor ve hata kanalı
    // kapatıyor; her silme kendi kanalında, olmayan ad atlanıyor.
    private static async Task DeleteAsync(RabbitMqConnection connection, Func<IChannel, Task> delete, CancellationToken ct)
    {
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct);

        try
        {
            await delete(channel);
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
        }
    }
}

/// <summary>Bir topolojinin adları; testte kurulabilsin diye topolojinin kendisiyle.</summary>
public sealed record TopologyNames(object Topology, IReadOnlyList<string> Queues, IReadOnlyList<string> Exchanges)
{
    public static TopologyNames Of(object topology)
    {
        var names = topology.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (property.Name, Value: (string)property.GetValue(topology)!))
            .ToList();

        return new TopologyNames(
            topology,
            names.Where(name => name.Name.EndsWith("Queue", StringComparison.Ordinal)).Select(name => name.Value).ToList(),
            names.Where(name => name.Name.EndsWith("Exchange", StringComparison.Ordinal)).Select(name => name.Value).ToList());
    }

    public Task DeclareAsync(IChannel channel, CancellationToken ct) =>
        (Task)Topology.GetType().GetMethod("DeclareAsync")!.Invoke(Topology, [channel, ct])!;
}

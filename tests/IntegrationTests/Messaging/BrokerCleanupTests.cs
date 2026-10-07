using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;

namespace HiWallet.IntegrationTests.Messaging;

/// <summary>
/// Koşunun broker'da açtığı exchange ve kuyruklar koşu bitince siliniyor. Testler test
/// ortamının broker'ına bağlanıyor; temizlenmeseler her koşunun artığı orada birikirdi.
/// </summary>
public sealed class BrokerCleanupTests
{
    private const string SkipReason = "RabbitMQ'ya ulaşılamıyor (RabbitMq__Host)";

    [Fact]
    public void Temizlik_BulunanHerTopolojininKuyrukVeExchangeAdlariniVeriyor()
    {
        var topologies = BrokerCleanup.Topologies(BrokerSettings.BuildOptions("hiwallet-tests-cleanup"));

        // Ad kuralı kayarsa (özellik "...Name" olursa) temizlik sessizce eksik kalırdı.
        Assert.NotEmpty(topologies);
        Assert.All(topologies, topology =>
        {
            Assert.NotEmpty(topology.Queues);
            Assert.NotEmpty(topology.Exchanges);
            Assert.All(topology.Queues.Concat(topology.Exchanges),
                name => Assert.StartsWith(BrokerSettings.NamePrefix, name));
        });
    }

    [Fact]
    public async Task Temizlik_OnekinButunKuyrukVeExchangeleriniSiler()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        // Koşunun kendi ön eki hâlâ kullanımda; bu test kendi ön ekiyle kurup siliyor.
        var options = BrokerSettings.BuildOptions(
            "hiwallet-tests-cleanup", namePrefix: $"it-{Guid.NewGuid().ToString("N")[..8]}.");
        var topologies = BrokerCleanup.Topologies(options);

        await using var connection = new RabbitMqConnection(Options.Create(options));

        await using (var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct))
        {
            foreach (var topology in topologies)
            {
                await topology.DeclareAsync(channel, ct);
            }
        }

        await BrokerCleanup.DeleteAsync(options, ct);

        foreach (var queue in topologies.SelectMany(topology => topology.Queues))
        {
            Assert.False(await QueueExistsAsync(connection, queue, ct), $"{queue} silinmedi");
        }

        foreach (var exchange in topologies.SelectMany(topology => topology.Exchanges))
        {
            Assert.False(await ExchangeExistsAsync(connection, exchange, ct), $"{exchange} silinmedi");
        }
    }

    // Pasif declare olmayan kuyrukta kanalı kapatıyor; her soru kendi kanalında.
    private static async Task<bool> QueueExistsAsync(RabbitMqConnection connection, string queue, CancellationToken ct)
    {
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct);

        try
        {
            await channel.QueueDeclarePassiveAsync(queue, ct);
            return true;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }
    }

    private static async Task<bool> ExchangeExistsAsync(
        RabbitMqConnection connection, string exchange, CancellationToken ct)
    {
        await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct);

        try
        {
            await channel.ExchangeDeclarePassiveAsync(exchange, ct);
            return true;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }
    }
}

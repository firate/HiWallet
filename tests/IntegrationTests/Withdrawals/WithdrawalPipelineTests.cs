using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Orchestrator'ın kendi tarafı uçtan uca: API → saga → outbox → relay → RabbitMQ,
/// ve ters yön → event tüketicisi → saga → bir sonraki komut.
///
/// Wallet ve bank-service henüz yok; bu testler onların YERİNE geçip kuyruktan komutu
/// okuyor ve cevabı yayınlıyor. Ölçülen şey orchestrator'ın hattı — karşı tarafın
/// davranışı değil.
///
/// <b>Broker yoksa atlanıyor.</b> Kurulumu zorunlu kılmak yerine, varsa doğrulanıyor.
/// Atlanan test yeşil görünmüyor; hangi güvencenin ölçülmediği çıktıdan okunabiliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalPipelineTests(OrchestratorFixture fixture) : IAsyncLifetime
{
    private const string SkipReason =
        "RabbitMQ'ya bağlanılamıyor (RabbitMq__Host ve kimlik bilgileri). Saga hattı doğrulanmadı.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private WithdrawalOrchestratorApiFactory? _app;
    private HttpClient? _client;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_app is not null) await _app.DisposeAsync();

        await DeleteTopologyAsync();
    }

    /// <summary>
    /// Mutlu yol. Her adımda saga'nın durumu ve outbox'a yazılan bir sonraki komut
    /// birlikte ilerliyor — ikisi hep aynı commit'te (decisions.md madde 32).
    /// </summary>
    [Fact]
    public async Task MutluYol_DebitedTenCompletedaKadarIlerler()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        var withdrawalId = await StartWithdrawalAsync(ct);

        // 1. Relay ilk komutu bankaya değil wallet'a gönderdi.
        var debit = await ReadCommandAsync<DebitForWithdrawal>(Topology.WalletQueue, ct);

        debit.SagaId.ShouldBe(withdrawalId);
        debit.Amount.ShouldBe(250.75m);

        // 2. Wallet cevap veriyor: para düşüldü, komisyonla birlikte 252.75.
        await PublishEventAsync(
            new WithdrawalDebited
            {
                SagaId = withdrawalId,
                LedgerTransactionId = Guid.NewGuid(),
                TotalDebited = 252.75m
            }, ct);

        // 3. Saga hem ilerledi hem bir sonraki komutu üretti.
        await WaitForStateAsync(withdrawalId, WithdrawalState.BankTransferPending, ct);

        var transfer = await ReadCommandAsync<StartBankTransfer>(Topology.BankQueue, ct);

        transfer.SagaId.ShouldBe(withdrawalId);

        // Bankaya giden tutar müşterinin İSTEDİĞİ tutar, düşülen toplam değil:
        // komisyon bizde kalıyor.
        transfer.Amount.ShouldBe(250.75m);
        transfer.DestinationIban.ShouldBe("TR330006100519786457841326");

        await using (var db = fixture.CreateContext())
        {
            var saga = await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct);

            // Komut kimliği saga'ya yazıldı: banka cevabı geldiğinde hangi komuta ait
            // olduğu bilinsin.
            saga.BankCommandId.ShouldBe(transfer.CommandId);
            saga.TotalDebited.ShouldBe(252.75m);
        }

        // 4. Banka başardı.
        await PublishEventAsync(
            new BankTransferSucceeded { SagaId = withdrawalId, BankReference = "BNK-1" }, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Completed, ct);
    }

    /// <summary>
    /// Telafi yolu. Banka kalıcı olarak başarısız olduğunda saga <c>Compensating</c>'e
    /// geçiyor ve iade komutunu üretiyor — TUTAR TAŞIMADAN, çünkü ters kayıt
    /// orijinalin aynası ve orijinali wallet yazdı.
    /// </summary>
    [Fact]
    public async Task TelafiYolu_BankaBasarisizOlunca_IadeKomutuUretilir()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        var withdrawalId = await StartWithdrawalAsync(ct);

        await ReadCommandAsync<DebitForWithdrawal>(Topology.WalletQueue, ct);

        await PublishEventAsync(
            new WithdrawalDebited
            {
                SagaId = withdrawalId,
                LedgerTransactionId = Guid.NewGuid(),
                TotalDebited = 252.75m
            }, ct);

        await ReadCommandAsync<StartBankTransfer>(Topology.BankQueue, ct);

        await PublishEventAsync(
            new BankTransferFailed { SagaId = withdrawalId, Reason = "hesap kapalı" }, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Compensating, ct);

        var refund = await ReadCommandAsync<RefundWithdrawal>(Topology.WalletQueue, ct);

        refund.SagaId.ShouldBe(withdrawalId);

        // Wallet iadeyi yazdı.
        await PublishEventAsync(
            new WithdrawalRefunded { SagaId = withdrawalId, LedgerTransactionId = Guid.NewGuid() }, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Failed, ct);

        await using var db = fixture.CreateContext();
        var saga = await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct);

        saga.FailureReason.ShouldBe("hesap kapalı");
        saga.RefundTransactionId.ShouldNotBeNull();
    }

    /// <summary>
    /// Broker en az bir kez teslim ediyor. Aynı event ikinci kez geldiğinde saga
    /// ilerlememeli VE ikinci bir komut üretilmemeli — üretilseydi banka aynı parayı
    /// iki kez gönderirdi.
    /// </summary>
    [Fact]
    public async Task TekrarEdenEvent_IkinciKomutUretmez()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        var withdrawalId = await StartWithdrawalAsync(ct);

        await ReadCommandAsync<DebitForWithdrawal>(Topology.WalletQueue, ct);

        var debited = new WithdrawalDebited
        {
            SagaId = withdrawalId,
            LedgerTransactionId = Guid.NewGuid(),
            TotalDebited = 252.75m
        };

        await PublishEventAsync(debited, ct);
        await WaitForStateAsync(withdrawalId, WithdrawalState.BankTransferPending, ct);

        var first = await ReadCommandAsync<StartBankTransfer>(Topology.BankQueue, ct);

        // Aynı event tekrar.
        await PublishEventAsync(debited, ct);

        // İkincisi yok sayılmalı: durum değişmiyor, version artmıyor, yeni komut yok.
        await using var db = fixture.CreateContext();

        var version = (await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct)).Version;

        await AssertStaysAsync(async () =>
        {
            await using var check = fixture.CreateContext();
            var saga = await check.Sagas.SingleAsync(s => s.Id == withdrawalId, ct);
            var commands = await check.Outbox.CountAsync(
                m => m.SagaId == withdrawalId && m.RoutingKey == nameof(StartBankTransfer), ct);

            return saga.State is WithdrawalState.BankTransferPending
                   && saga.Version == version
                   && commands == 1;
        }, ct);

        // Kimlik de değişmemeli: tekrar gönderim aynı komut olmalı.
        (await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct))
            .BankCommandId.ShouldBe(first.CommandId);
    }

    /// <summary>
    /// Çelişki. Hiç düşme yapılmamış bir saga'ya "banka gönderdi" demek yok sayılacak
    /// bir tekrar değil; saga olduğu yerde kalıyor ve mesaj dead-letter'a gidiyor
    /// (decisions.md madde 31).
    /// </summary>
    [Fact]
    public async Task Celiski_SagayiDegistirmez_MesajDeadLetteraGider()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        var withdrawalId = await StartWithdrawalAsync(ct);

        await PublishEventAsync(
            new BankTransferSucceeded { SagaId = withdrawalId, BankReference = "BNK-X" }, ct);

        await WaitForAsync(async () => await CountDeadLetteredAsync(ct) > 0,
            "çelişkili mesaj dead-letter kuyruğuna düşmedi");

        await using var db = fixture.CreateContext();
        var saga = await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct);

        saga.State.ShouldBe(WithdrawalState.Initiated);
        saga.Version.ShouldBe(0);
    }

    // --- yardımcılar ---------------------------------------------------------

    private static WithdrawalTopology Topology { get; } =
        new(Options.Create(BrokerSettings.BuildOptions("hiwallet-tests")));

    /// <summary>
    /// Topolojiyi test kendisi kuruyor. Uygulama da kuruyor (declare idempotent) ama
    /// bunu ARKA PLANDA yapıyor: POST döndüğünde relay henüz bağlanmamış olabilir ve
    /// kuyruğu okumaya çalışan test <c>NOT_FOUND</c> alırdı. Bekleyen bir yarış değil,
    /// testin kendi ön koşulu.
    /// </summary>
    private static async Task EnsureTopologyAsync(CancellationToken ct)
    {
        await using var connection = new RabbitMqConnection(
            Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-topology")));

        await using var channel = await (await connection.GetAsync(ct))
            .CreateChannelAsync(cancellationToken: ct);

        await Topology.DeclareAsync(channel, ct);
    }

    private async Task<Guid> StartWithdrawalAsync(CancellationToken ct)
    {
        await EnsureTopologyAsync(ct);

        _app ??= new WithdrawalOrchestratorApiFactory(fixture, useRealBroker: true);
        _client ??= _app.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId = Guid.NewGuid(),
                walletId = Guid.NewGuid(),
                amount = 250.75m,
                currency = "TRY",
                destinationIban = "TR33 0006 1005 1978 6457 8413 26"
            })
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await _client.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AcceptedBody>(JsonOptions, ct);

        return body.ShouldNotBeNull().WithdrawalId;
    }

    /// <summary>
    /// Kuyruktan tek komut çeker. <c>BasicGet</c> yeterli: burada tüketici davranışı
    /// değil, relay'in mesajı doğru kuyruğa doğru içerikle koyduğu ölçülüyor.
    /// </summary>
    private async Task<T> ReadCommandAsync<T>(string queue, CancellationToken ct)
    {
        await using var connection = new RabbitMqConnection(
            Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-reader")));

        await using var channel = await (await connection.GetAsync(ct))
            .CreateChannelAsync(cancellationToken: ct);

        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true, ct);

            if (result is not null)
            {
                result.RoutingKey.ShouldBe(typeof(T).Name);

                // MessageId alıcı tarafın deduplikasyon anahtarı; outbox satırının
                // id'siyle aynı olmak zorunda.
                result.BasicProperties.MessageId.ShouldNotBeNullOrWhiteSpace();

                return JsonSerializer.Deserialize<T>(result.Body.Span, JsonOptions)
                       ?? throw new InvalidOperationException($"{typeof(T).Name} gövdesi boş.");
            }

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"{Timeout.TotalSeconds} sn içinde {queue} kuyruğuna {typeof(T).Name} düşmedi.");
    }

    private async Task PublishEventAsync<T>(T @event, CancellationToken ct)
    {
        await using var connection = new RabbitMqConnection(
            Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-publisher")));

        await using var channel = await (await connection.GetAsync(ct))
            .CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                ct);

        await channel.BasicPublishAsync(
            exchange: Topology.Exchange,
            routingKey: typeof(T).Name,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, Type = typeof(T).Name },
            body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, JsonOptions)),
            cancellationToken: ct);
    }

    private async Task<uint> CountDeadLetteredAsync(CancellationToken ct)
    {
        await using var connection = new RabbitMqConnection(
            Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-dlq")));

        await using var channel = await (await connection.GetAsync(ct))
            .CreateChannelAsync(cancellationToken: ct);

        return await channel.MessageCountAsync(Topology.DeadLetterQueue, ct);
    }

    private async Task WaitForStateAsync(Guid sagaId, WithdrawalState expected, CancellationToken ct)
    {
        await WaitForAsync(async () =>
        {
            await using var db = fixture.CreateContext();
            var saga = await db.Sagas.AsNoTracking().SingleAsync(s => s.Id == sagaId, ct);

            return saga.State == expected;
        }, $"saga {expected} durumuna geçmedi");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;

            await Task.Delay(200);
        }

        throw new TimeoutException($"{Timeout.TotalSeconds} sn içinde {failureMessage}.");
    }

    /// <summary>
    /// Koşul bir süre boyunca DOĞRU KALMALI. "Bir şey olmadı"yı doğrulamanın tek yolu
    /// beklemek — anında bakılsaydı test mesaj işlenmeden geçerdi.
    /// </summary>
    private static async Task AssertStaysAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            (await condition()).ShouldBeTrue();
            await Task.Delay(300, ct);
        }
    }

    /// <summary>
    /// Koşunun açtığı exchange ve kuyrukları siler. Ön ek koşuya özel olduğu için
    /// bırakılsalardı broker'da her koşuda yeni bir çöp yığını birikirdi.
    /// </summary>
    private static async Task DeleteTopologyAsync()
    {
        if (!BrokerSettings.Configured) return;

        try
        {
            await using var connection = new RabbitMqConnection(
                Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-cleanup")));

            await using var channel = await (await connection.GetAsync(CancellationToken.None))
                .CreateChannelAsync();

            foreach (var queue in new[]
                     {
                         Topology.WalletQueue, Topology.BankQueue,
                         Topology.OrchestratorQueue, Topology.DeadLetterQueue
                     })
            {
                await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            }

            await channel.ExchangeDeleteAsync(Topology.Exchange, ifUnused: false);
            await channel.ExchangeDeleteAsync(Topology.DeadLetterExchange, ifUnused: false);
        }
        catch (Exception)
        {
            // Temizlik testin sonucunu etkilememeli.
        }
    }

    private sealed record AcceptedBody(Guid WithdrawalId, string State, bool Replayed);
}

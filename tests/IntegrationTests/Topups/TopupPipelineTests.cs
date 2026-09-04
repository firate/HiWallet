using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.TopupWebhook.Application;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Uçtan uca: webhook → inbox → relay → RabbitMQ → tüketici → ledger.
/// Zincirin tamamı gerçek; iki uygulama ayrı ayrı ayağa kalkıyor ve aralarında
/// yalnızca broker var.
///
/// <b>Broker yoksa atlanıyor.</b> Kurulumu zorunlu kılmak yerine, varsa doğrulanıyor
/// (<c>AppRolePrivilegeTests</c> ile aynı yaklaşım). Atlanan test yeşil görünmüyor,
/// "skipped" görünüyor — hangi güvencenin ölçülmediği çıktıdan okunabiliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TopupPipelineTests(PostgresFixture postgres, InboxFixture inbox) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private TopupWebhookApiFactory? _webhook;
    private WalletApiFactory? _wallet;
    private HttpClient? _client;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_webhook is not null) await _webhook.DisposeAsync();
        if (_wallet is not null) await _wallet.DisposeAsync();

        await DeleteTopologyAsync();
    }

    [Fact]
    public async Task Webhook_LedgeraKadarGider_VeTekrariBakiyeyiIkinciKezArtirmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(
            await BrokerSettings.IsReachableAsync(ct),
            "RabbitMQ erişilemiyor (RabbitMq__Host / kimlik bilgileri). Uçtan uca hat doğrulanmadı.");

        await StartAsync();

        var walletId = await NewWalletAsync(ct);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var payload = Payload(eventId, walletId, 123.45m);

        var response = await PostSignedAsync(payload, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Relay yayınlayana ve tüketici işleyene kadar bekle. Süre değil KOŞUL
        // bekleniyor: sabit bir Task.Delay ya yavaş makinede yetmez ya da hızlı
        // makinede koşuyu boşuna uzatır.
        await WaitUntilAsync(
            async () => await BalanceAsync(walletId, ct) == 123.45m,
            "bakiye 123.45 olmadı",
            ct);

        await using (var db = postgres.CreateContext())
        {
            var tx = await db.LedgerTransactions.SingleAsync(
                t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Topup, ct);

            var entries = await db.LedgerEntries.Where(e => e.TransactionId == tx.Id).ToListAsync(ct);

            entries.Sum(e => e.Amount).ShouldBe(0m);
            entries.Single(e => e.LedgerAccountId == walletId).Amount.ShouldBe(123.45m);

            // Mesaj tüketildiği için inbox satırı da yayınlanmış olmalı.
            await using var inboxDb = inbox.CreateContext();
            var row = await inboxDb.Inbox.SingleAsync(m => m.EventId == eventId, ct);
            row.PublishedAt.ShouldNotBeNull();
            row.PublishAttempts.ShouldBe(1);
        }

        // Sağlayıcı aynı webhook'u tekrar gönderiyor — gerçekte sık olan senaryo.
        // İki kademe de devrede: inbox onu hiç kuyruğa koymuyor, koysaydı bile
        // processed_events yutardı.
        var replay = await PostSignedAsync(payload, ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        (await BalanceAsync(walletId, ct)).ShouldBe(123.45m, "tekrar eden webhook bakiyeyi ikinci kez artırdı");
    }

    [Fact]
    public async Task AyniCuzdaninMesajlari_HepsiIslenir_VeToplamDogru()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(
            await BrokerSettings.IsReachableAsync(ct),
            "RabbitMQ erişilemiyor. Uçtan uca hat doğrulanmadı.");

        await StartAsync();

        var walletId = await NewWalletAsync(ct);
        const int count = 20;

        for (var i = 0; i < count; i++)
        {
            var response = await PostSignedAsync(
                Payload($"evt_{Guid.NewGuid():N}", walletId, 10.00m), ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await WaitUntilAsync(
            async () => await BalanceAsync(walletId, ct) == count * 10.00m,
            $"bakiye {count * 10.00m} olmadı",
            ct);

        await using var db = postgres.CreateContext();

        // Hepsi aynı cüzdana, yani aynı partition'a düştü ve sırayla işlendi.
        (await db.LedgerTransactions.CountAsync(
            t => t.LedgerAccountId == walletId && t.Type == LedgerTransactionType.Topup, ct))
            .ShouldBe(count);
    }

    private async Task StartAsync()
    {
        _webhook = new TopupWebhookApiFactory(inbox);
        _wallet = new WalletApiFactory(postgres);

        _client = _webhook.CreateClient();

        // Tüketici hosted service; istemci oluşturmak host'u da başlatıyor.
        _ = _wallet.CreateClient();

        // Tüketicinin kuyruklara abone olması bir tur alıyor; abone olmadan
        // yayınlanan mesaj kaybolmaz (kuyruk durable) ama beklemek testi
        // gereksiz yere zamanaşımına yaklaştırmıyor.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
    }

    private static object Payload(string eventId, Guid walletId, decimal amount) => new
    {
        eventId,
        walletId,
        amount,
        currency = "TRY",
        reference = "pi_pipeline",
        occurredAt = DateTimeOffset.Parse("2026-03-01T10:00:00+00:00")
    };

    private async Task<HttpResponseMessage> PostSignedAsync(object payload, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/v1/webhooks/topup/{TopupWebhookApiFactory.StripeProvider}")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(
            WebhookSignature.HeaderName,
            WebhookSignature.Compute(body, TopupWebhookApiFactory.StripeSecret));

        return await _client!.SendAsync(request, ct);
    }

    private async Task<decimal> BalanceAsync(Guid walletId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.LedgerBalances
            .Where(b => b.LedgerAccountId == walletId)
            .Select(b => b.Balance)
            .SingleAsync(ct);
    }

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, accountId, "Hat testi", ct);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition, string failureMessage, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }

        throw new TimeoutException($"{Timeout.TotalSeconds} sn içinde {failureMessage}.");
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
            var options = Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-cleanup"));
            var topology = new TopupTopology(options);

            await using var connection = new RabbitMqConnection(options);
            await using var channel = await (await connection.GetAsync(CancellationToken.None))
                .CreateChannelAsync();

            for (var partition = 0; partition < topology.PartitionCount; partition++)
            {
                await channel.QueueDeleteAsync(topology.PartitionQueue(partition), ifUnused: false, ifEmpty: false);
            }

            await channel.QueueDeleteAsync(topology.DeadLetterQueue, ifUnused: false, ifEmpty: false);
            await channel.ExchangeDeleteAsync(topology.Exchange, ifUnused: false);
            await channel.ExchangeDeleteAsync(topology.DeadLetterExchange, ifUnused: false);
        }
        catch (Exception)
        {
            // Temizlik testin sonucunu etkilememeli.
        }
    }
}

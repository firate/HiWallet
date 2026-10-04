using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.BankAdapter.Application;
using HiWallet.BankAdapter.Infrastructure.Jobs;
using HiWallet.BankIntegration.Persistence;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Hesabımıza gelen havalenin banka tarafı: bildirim (asıl yol) ve hesap hareketi
/// taraması (kontrol) aynı kayda varıyor, aynı havale iki kez yazılmıyor. Wallet'a
/// gidecek mesaj kayıtla birlikte saklanıyor; gönderenin adı ve IBAN'ı onda yok.
/// Broker gerekmiyor: yayın relay'in işi, burada sınanan şey kayıt.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankDepositTests(BankFixture bankDb) : IAsyncLifetime
{
    private const string SenderName = "Ayşe Yılmaz";
    private const string SenderIban = "TR330006100519786457841326";
    private const string SenderNationalId = "10000000146";

    private BankFakeFactory _bankFake = null!;

    public ValueTask InitializeAsync()
    {
        _bankFake = new BankFakeFactory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _bankFake.DisposeAsync();

    private DepositRecorder Recorder() =>
        new(bankDb.ContextFactory, TimeProvider.System, NullLogger<DepositRecorder>.Instance);

    private BankNotificationHandler Notifications() => new(
        new TransferCompleter(bankDb.ContextFactory, TimeProvider.System, NullLogger<TransferCompleter>.Instance),
        Recorder(),
        NullLogger<BankNotificationHandler>.Instance);

    private DepositScan Scan(DateTimeOffset now) => new(
        new BankClient(new SingleClientFactory(_bankFake.CreateClient()), NullLogger<BankClient>.Instance),
        Recorder(),
        Options.Create(new BankAdapterOptions { BaseUrl = "http://bank-fake", Provider = TestBankSecrets.Bank }),
        new JobLease(bankDb.ConnectionString, NullLogger<JobLease>.Instance),
        new FixedTimeProvider(now),
        NullLogger<DepositScan>.Instance);

    private static string IncomingNotification(string bankReference, string type = "transfer.incoming") =>
        JsonSerializer.Serialize(new
        {
            eventId = $"evt-{bankReference}",
            type,
            bankReference,
            amount = 250.00m,
            currency = "TRY",
            description = "481 730 5925",
            senderName = SenderName,
            senderIban = SenderIban,
            senderNationalId = SenderNationalId,
            occurredAt = DateTimeOffset.UtcNow
        });

    private static string NewReference() => $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant();

    private async Task<List<BankDeposit>> DepositsAsync(string bankReference, CancellationToken ct)
    {
        await using var db = bankDb.CreateContext();
        return await db.Deposits.AsNoTracking().Where(d => d.BankReference == bankReference).ToListAsync(ct);
    }

    private async Task<string> ReceiveAsync(bool notify, CancellationToken ct)
    {
        using var client = _bankFake.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/incoming-transfers", new
        {
            amount = 120m,
            currency = "TRY",
            description = "4817305925",
            senderName = SenderName,
            senderIban = SenderIban,
            senderNationalId = SenderNationalId,
            notify
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("bankReference").GetString()!;
    }

    [Fact]
    public async Task GelenHavaleBildirimi_Kaydedilir_WalletMesajiSaklanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = NewReference();

        await Notifications().ApplyAsync(TestBankSecrets.Bank, IncomingNotification(reference), ct);

        var deposit = (await DepositsAsync(reference, ct)).ShouldHaveSingleItem();
        deposit.DiscoveredVia.ShouldBe(DepositRecorder.ViaCallback);
        deposit.PublishedAt.ShouldBeNull();
        deposit.SenderName.ShouldBe(SenderName);
        deposit.SenderIban.ShouldBe(SenderIban);

        var message = JsonSerializer.Deserialize<BankDepositReceived>(deposit.Payload, JsonSerializerOptions.Web)!;
        message.Provider.ShouldBe(TestBankSecrets.Bank);
        message.BankReference.ShouldBe(reference);
        message.Amount.ShouldBe(250.00m);
        message.Currency.ShouldBe("TRY");
        message.Description.ShouldBe("481 730 5925");
        message.SenderNationalId.ShouldBe(SenderNationalId);

        // Gönderenin adı ve IBAN'ı wallet'a gitmiyor; kişisel veri burada kalıyor.
        deposit.Payload.ShouldNotContain(SenderName);
        deposit.Payload.ShouldNotContain(SenderIban);
    }

    /// <summary>Aynı havale bildirimle iki kez, sonra taramayla da gelse tek kayıt.</summary>
    [Fact]
    public async Task AyniHavale_IkinciKezYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = NewReference();

        await Notifications().ApplyAsync(TestBankSecrets.Bank, IncomingNotification(reference), ct);
        await Notifications().ApplyAsync(TestBankSecrets.Bank, IncomingNotification(reference), ct);

        var recordedAgain = await Recorder().RecordAsync(
            TestBankSecrets.Bank,
            new IncomingTransferItem(reference, 250m, "TRY", null, null, null, null, DateTimeOffset.UtcNow),
            DepositRecorder.ViaReconciliation,
            ct);

        recordedAgain.ShouldBeFalse();
        (await DepositsAsync(reference, ct)).ShouldHaveSingleItem().DiscoveredVia.ShouldBe(DepositRecorder.ViaCallback);
    }

    /// <summary>Tanınmayan bildirim tipi tahmin edilmiyor: satır inbox'ta kalıyor ve alarm oluyor.</summary>
    [Fact]
    public async Task TaninmayanBildirimTipi_HataVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = NewReference();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Notifications().ApplyAsync(TestBankSecrets.Bank, IncomingNotification(reference, "transfer.reversed"), ct));

        (await DepositsAsync(reference, ct)).ShouldBeEmpty();
    }

    /// <summary>
    /// Bildirimi kaçırılmış havaleyi hesap hareketleri buluyor. Tarama bir kontrol: ikinci
    /// tur aynı havaleyi yeniden yazmıyor.
    /// </summary>
    [Fact]
    public async Task Tarama_BildirimiKacirilanHavaleyiBulur()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = await ReceiveAsync(notify: false, ct);
        var later = DateTimeOffset.UtcNow.AddHours(1);

        (await Scan(later).ScanAsync(ct)).ShouldBe(1);
        (await Scan(later).ScanAsync(ct)).ShouldBe(0);

        var deposit = (await DepositsAsync(reference, ct)).ShouldHaveSingleItem();
        deposit.DiscoveredVia.ShouldBe(DepositRecorder.ViaReconciliation);
        deposit.Provider.ShouldBe(TestBankSecrets.Bank);
        deposit.Amount.ShouldBe(120m);
    }

    /// <summary>Yeni havale taranmıyor: bildirimi yolda olabilir.</summary>
    [Fact]
    public async Task Tarama_YeniHavaleyiBildirimeBirakir()
    {
        var ct = TestContext.Current.CancellationToken;
        var reference = await ReceiveAsync(notify: false, ct);

        (await Scan(DateTimeOffset.UtcNow).ScanAsync(ct)).ShouldBe(0);
        (await DepositsAsync(reference, ct)).ShouldBeEmpty();
    }

    /// <summary>
    /// Asıl yol uçtan uca: sahte banka bildirimi imzalayıp bank-webhook'a gönderiyor,
    /// webhook inbox'a yazıyor, adaptörün yorumlayıcısı kaydediyor. İki taraf sözleşmeyi
    /// ayrı tiplerle yazıyor; ayrışırlarsa burası kırılıyor.
    /// </summary>
    [Fact]
    public async Task SahteBankaninBildirimi_WebhooktanGecip_Kaydedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var webhook = new BankWebhookFactory(bankDb);
        await using var bank = new BankFakeFactory(
            callbackUrl: $"http://bank-webhook/v1/webhooks/bank/{TestBankSecrets.Bank}",
            callbackHttpClient: webhook.CreateClient());

        using var client = bank.CreateClient();
        var accepted = await client.PostAsJsonAsync("/v1/incoming-transfers", new
        {
            amount = 75m,
            currency = "TRY",
            description = "4817305925",
            senderName = SenderName,
            senderIban = SenderIban,
            senderNationalId = SenderNationalId
        }, ct);
        var reference = (await accepted.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("bankReference").GetString()!;

        BankCallback? callback = null;

        for (var attempt = 0; attempt < 50 && callback is null; attempt++)
        {
            await using var db = bankDb.CreateContext();
            callback = await db.Callbacks.AsNoTracking().FirstOrDefaultAsync(c => c.EventId == $"evt-{reference}", ct);

            if (callback is null) await Task.Delay(100, ct);
        }

        callback.ShouldNotBeNull("Bildirim beş saniyede inbox'a ulaşmadı.");

        await Notifications().ApplyAsync(callback.Provider, callback.RawPayload, ct);

        var deposit = (await DepositsAsync(reference, ct)).ShouldHaveSingleItem();
        deposit.Amount.ShouldBe(75m);
        deposit.SenderNationalId.ShouldBe(SenderNationalId);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

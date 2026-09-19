using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.Fakes.Topups;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Sahte sağlayıcının <c>topup-webhook</c>'u tetiklemesi.
///
/// <b>Bu dosyaya kadar top-up hattının dış endpoint'i hiç sınanmamıştı.</b> Testler
/// gövdeyi kendi kurup HMAC'i kendi hesaplayıp POST ediyordu — yani sınanan şey
/// webhook'un davranışıydı, sağlayıcının değil. Şimdi araya gerçek bir HTTP
/// sınırı ve gerçek bir imzalayan giriyor.
///
/// Kazanç somut: <c>overview.md</c> madde 9'daki dört senaryodan
/// <b>sırasız gönderim</b> ilk kez koşturulabiliyor. Elle iki webhook'u ters
/// sırada göndermek pratik değildi.
///
/// Ledger'a kadar gitmiyor — orası <see cref="TopupPipelineTests"/>'in işi.
/// Burada sınanan şey inbox'a ne düştüğü.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FakeProviderTests(InboxFixture inbox) : IAsyncLifetime
{
    private TopupWebhookApiFactory _webhook = null!;
    private StripeFakeFactory _provider = null!;
    private HttpClient _providerClient = null!;

    public ValueTask InitializeAsync()
    {
        _webhook = new TopupWebhookApiFactory(inbox);
        _provider = new StripeFakeFactory(_webhook.CreateClient());
        _providerClient = _provider.CreateClient();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _providerClient.Dispose();
        await _provider.DisposeAsync();
        await _webhook.DisposeAsync();
    }

    private async Task<int> TriggerAsync(
        Guid walletId, TopupDeliveryMode mode, int count = 3, decimal amount = 100m)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _providerClient.PostAsJsonAsync("/v1/topups", new
        {
            walletId,
            amount,
            currency = "TRY",
            mode = mode.ToString(),
            count,
            delayMilliseconds = 200
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        return body.GetProperty("eventCount").GetInt32();
    }

    /// <summary>
    /// Cüzdanın satırları <c>routing_key</c> üzerinden bulunuyor: top-up'ta partition
    /// anahtarı cüzdan kimliği. <c>payload</c> üzerinde metin araması YAPILAMIYOR —
    /// kolon <c>jsonb</c> ve EF'in <c>Contains</c> çevirisi olan <c>LIKE</c> orada yok.
    ///
    /// Gönderim ARKA PLANDA: endpoint <c>202</c> dönüp çekiliyor. Bu yüzden inbox'a
    /// bakmadan önce beklemek gerekiyor — gerçek bir sağlayıcıda da webhook
    /// senin request'inle aynı anda gelmiyor.
    /// </summary>
    private async Task<List<string>> WaitForInboxAsync(
        Guid walletId, int expected, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using (var db = inbox.CreateContext())
            {
                var rows = await db.Inbox
                    .AsNoTracking()
                    .Where(m => m.RoutingKey == walletId.ToString())
                    .Select(m => m.EventId)
                    .ToListAsync(ct);

                if (rows.Count >= expected) return rows;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"{walletId} için {expected} satır beş saniyede gelmedi.");
    }

    [Fact]
    public async Task Normal_TekSatirInboxaDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = Guid.NewGuid();

        var expected = await TriggerAsync(wallet, TopupDeliveryMode.Normal);

        expected.ShouldBe(1);

        var rows = await WaitForInboxAsync(wallet, 1, ct);

        rows.Count.ShouldBe(1);
    }

    /// <summary>
    /// <b>Aynı event iki kez gönderiliyor, inbox'a BİR satır düşüyor.</b>
    ///
    /// Sağlayıcının yeniden göndermesi beklenen bir davranış; ikinci gönderim
    /// <c>(provider, event_id)</c> UNIQUE'e takılıyor ve kuyruğa hiç konmuyor.
    /// İki <c>eventId</c> farklı olsaydı bu iki ayrı para girişi olurdu ve test
    /// idempotency'yi değil toplamayı ölçerdi.
    /// </summary>
    [Fact]
    public async Task Duplicate_IkiKezGonderilir_InboxaTekSatirDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = Guid.NewGuid();

        var expected = await TriggerAsync(wallet, TopupDeliveryMode.Duplicate);

        expected.ShouldBe(2, "iki webhook gidiyor");

        var rows = await WaitForInboxAsync(wallet, 1, ct);

        // İkinci gönderimin de ulaşmasını bekle, sonra hâlâ tek satır mı bak.
        await Task.Delay(500, ct);

        await using var db = inbox.CreateContext();

        var count = await db.Inbox
            .AsNoTracking()
            .CountAsync(m => m.RoutingKey == wallet.ToString(), ct);

        count.ShouldBe(1, "aynı event_id ikinci satır açmamalı");
        rows.Distinct().Count().ShouldBe(1);
    }

    /// <summary>
    /// <b>Madde 9'un hiç koşturulamamış senaryosu.</b>
    ///
    /// Aynı cüzdana N event, en yenisi önce gönderiliyor. Hepsi iniyor ve hiçbiri
    /// kaybolmuyor.
    ///
    /// Bu test "sıra korunuyor" DEMİYOR: top-up'ta toplama değişmeli, sıra nihai
    /// bakiyeyi etkilemiyor. Dediği şey, ters sırada gelen bir dizinin tamamının
    /// kabul edildiği — ve gerçek değeri ledger'a kadar gittiğinde ortaya çıkıyor:
    /// hepsi aynı cüzdana ait olduğu için consistent-hash routing onları tek
    /// partition'a düşürüyor ve tek tüketici sırayla işliyor. Dağılsalardı aynı
    /// cüzdan üzerinde eşzamanlı yazma olur, optimistic lock çakışmaları başlardı.
    /// </summary>
    [Fact]
    public async Task OutOfOrder_TersSiradaGonderilir_HepsiInboxaDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = Guid.NewGuid();

        var expected = await TriggerAsync(wallet, TopupDeliveryMode.OutOfOrder, count: 5);

        expected.ShouldBe(5);

        var rows = await WaitForInboxAsync(wallet, 5, ct);

        rows.Count.ShouldBe(5);
        rows.Distinct().Count().ShouldBe(5, "her event ayrı kimlik taşımalı");
    }

    /// <summary>
    /// Gecikmeli gönderim: tetikleme döndüğünde inbox HENÜZ BOŞ. Eventual
    /// davranışın görünür hali.
    /// </summary>
    [Fact]
    public async Task Delayed_TetiklemeDondugunde_InboxHenuzBos()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = Guid.NewGuid();

        await TriggerAsync(wallet, TopupDeliveryMode.Delayed);

        await using (var db = inbox.CreateContext())
        {
            (await db.Inbox.AsNoTracking()
                .CountAsync(m => m.RoutingKey == wallet.ToString(), ct))
                .ShouldBe(0, "202 döndüğünde webhook henüz gönderilmedi");
        }

        var rows = await WaitForInboxAsync(wallet, 1, ct);

        rows.Count.ShouldBe(1);
    }

    /// <summary>
    /// Sağlayıcı YANLIŞ secret ile imzalarsa webhook <c>401</c> alıyor ve inbox'a
    /// hiçbir şey yazılmıyor.
    ///
    /// Bu test olmadan diğerleri bir şey kanıtlamazdı: imza doğrulaması hiç
    /// çalışmasa da hepsi geçerdi.
    /// </summary>
    [Fact]
    public async Task YanlisSecret_InboxaHicbirSeyYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = Guid.NewGuid();

        await using var badProvider = new StripeFakeFactory(
            _webhook.CreateClient(), secret: "yanlis-secret");

        using var client = badProvider.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/topups", new
        {
            walletId = wallet,
            amount = 100m,
            currency = "TRY",
            mode = nameof(TopupDeliveryMode.Normal)
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, "sağlayıcı gönderdiğini bilmiyor");

        // Gönderimin gerçekleşmesine ve reddedilmesine süre tanı.
        await Task.Delay(1000, ct);

        await using var db = inbox.CreateContext();

        (await db.Inbox.AsNoTracking()
            .CountAsync(m => m.RoutingKey == wallet.ToString(), ct))
            .ShouldBe(0, "geçersiz imza inbox'a satır bırakmamalı");
    }

    [Fact]
    public async Task GecersizGirdi_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _providerClient.PostAsJsonAsync("/v1/topups", new
        {
            walletId = Guid.Empty,
            amount = -5m,
            currency = "TL",
            mode = nameof(TopupDeliveryMode.Normal)
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

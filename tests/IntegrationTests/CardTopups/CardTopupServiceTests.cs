using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.CardTopup.Application;
using HiWallet.CardTopup.Domain;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.CardTopup.Infrastructure.Upstream;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.CardPayments;
using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.CardTopups;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Topup = HiWallet.CardTopup.Domain.CardTopup;

namespace HiWallet.IntegrationTests.CardTopups;

/// <summary>
/// Kart yüklemesi servisi baştan sona, broker olmadan: wallet-api ve sahte sağlayıcı bellekte
/// gerçek HTTP ile çağrılıyor, pay wallet'ın veritabanına yazılıyor, ödeme sahte sağlayıcıda
/// açılıyor. Kapanış outbox'tan okunup wallet'ın handler'ına veriliyor: iki servisin mesaj
/// sözleşmesi de sınanıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardTopupServiceTests(PostgresFixture postgres, CardTopupFixture cards) : IAsyncLifetime
{
    private const string ReturnUrl = "https://app.test/kart-yukleme";

    private readonly SettableTimeProvider _providerTime = new(DateTimeOffset.UtcNow);

    private WalletApiFactory _walletApi = null!;
    private StripeFakeFactory _provider = null!;
    private CardTopupApiFactory _factory = null!;
    private Guid _account;
    private Guid _wallet;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            _account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Unverified);
            _wallet = await LedgerSeeder.CreateWalletAsync(db, _account, "Ana", ct);
        }

        _walletApi = new WalletApiFactory(postgres);
        _provider = new StripeFakeFactory(new HttpClient(new UnreachableHandler()), time: _providerTime);
        _factory = Service(wallet: true, provider: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _provider.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private CardTopupApiFactory Service(bool wallet, bool provider) => new(
        cards,
        wallet ? new PassthroughHandler(_walletApi.CreateClient()) : null,
        provider ? new PassthroughHandler(_provider.CreateClient()) : null);

    private static async Task<HttpResponseMessage> StartAsync(
        CardTopupApiFactory factory, Guid account, Guid wallet, decimal amount, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/card-topups")
        {
            Content = JsonContent.Create(new { walletId = wallet, amount, currency = "TRY", returnUrl = ReturnUrl })
        };

        request.Headers.Add("Idempotency-Key", key);

        return await factory.CreateClient().AsOwnerOf(account).SendAsync(request, ct);
    }

    private Task<HttpResponseMessage> StartAsync(decimal amount, string key, CancellationToken ct) =>
        StartAsync(_factory, _account, _wallet, amount, key, ct);

    private async Task<Topup> TopupAsync(Guid id, CancellationToken ct)
    {
        await using var db = cards.CreateContext();
        return await db.CardTopups.AsNoTracking().SingleAsync(c => c.Id == id, ct);
    }

    private async Task<List<CardTopupClosed>> ClosuresAsync(Guid id, CancellationToken ct)
    {
        await using var db = cards.CreateContext();

        var payloads = await db.Outbox
            .AsNoTracking()
            .Where(m => m.CardTopupId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Payload)
            .ToListAsync(ct);

        return [.. payloads.Select(p => JsonSerializer.Deserialize<CardTopupClosed>(p, JsonSerializerOptions.Web)!)];
    }

    private async Task<decimal> WalletBalanceAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances.Where(b => b.LedgerAccountId == _wallet).SumAsync(b => b.Balance, ct);
    }

    private async Task<Guid> PendingAsync(decimal amount, CancellationToken ct)
    {
        var response = await StartAsync(amount, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("state").GetString().ShouldBe("pending");

        return body.GetProperty("cardTopupId").GetGuid();
    }

    private async Task<TransitionResult> NotifyAsync(
        Guid id, string type, CancellationToken ct, decimal? amount = null, string? paymentId = null)
    {
        var topup = await TopupAsync(id, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ApplyCardPaymentHandler>();

        return await handler.HandleAsync(new CardPaymentUpdated
        {
            Provider = topup.Provider,
            EventId = Guid.NewGuid().ToString("N"),
            Type = type,
            PaymentId = paymentId ?? topup.PaymentId!,
            Reference = id,
            Amount = amount ?? topup.Amount,
            Currency = topup.Currency,
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);
    }

    private OpenCardTopupScanner Scanner(CardTopupApiFactory factory, DateTimeOffset now) => new(
        factory.Services.GetRequiredService<IDbContextFactory<CardTopupDbContext>>(),
        factory.Services.GetRequiredService<CardTopupTransitions>(),
        factory.Services.GetRequiredService<CardPaymentClient>(),
        new FixedTimeProvider(now),
        factory.Services.GetRequiredService<IOptions<CardTopupOptions>>(),
        NullLogger<OpenCardTopupScanner>.Instance);

    // ------------------------------------------------------------------
    // Geçmiş
    // ------------------------------------------------------------------
    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("cardTopupId").GetGuid())];

    [Fact]
    public async Task Gecmis_CuzdaninYuklemeleriYenidenEskiye()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = (await (await StartAsync(100m, "gecmis-1", ct)).Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("cardTopupId").GetGuid();
        var second = (await (await StartAsync(200m, "gecmis-2", ct)).Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("cardTopupId").GetGuid();

        var page = await _factory.CreateClient().AsOwnerOf(_account)
            .GetFromJsonAsync<JsonElement>($"/v1/wallets/{_wallet}/card-topups", ct);
        var paged = await _factory.CreateClient().AsOwnerOf(_account)
            .GetFromJsonAsync<JsonElement>($"/v1/wallets/{_wallet}/card-topups?size=1", ct);

        Ids(page).ShouldBe([second, first]);
        page.GetProperty("items")[0].GetProperty("state").GetString().ShouldBe("pending");
        Ids(paged).ShouldBe([second]);
        paged.GetProperty("nextCursor").GetGuid().ShouldBe(second);
    }

    /// <summary>Başkasının cüzdanında liste boş: cüzdanın var olduğu da söylenmiyor.</summary>
    [Fact]
    public async Task Gecmis_BaskasiGoremez_CalisanGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        (await StartAsync(100m, "gecmis-baskasi", ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var other = await _factory.CreateClient().As($"baska-{Guid.NewGuid():N}")
            .GetFromJsonAsync<JsonElement>($"/v1/wallets/{_wallet}/card-topups", ct);
        var staff = await _factory.CreateClient()
            .AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView)
            .GetFromJsonAsync<JsonElement>($"/v1/wallets/{_wallet}/card-topups", ct);
        var unauthorized = await _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}")
            .GetAsync($"/v1/wallets/{_wallet}/card-topups", ct);

        other.GetProperty("items").GetArrayLength().ShouldBe(0);
        staff.GetProperty("items").GetArrayLength().ShouldBe(1);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // Başlatma
    // ------------------------------------------------------------------
    [Fact]
    public async Task AnahtarYok_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _factory.CreateClient().AsOwnerOf(_account).PostAsJsonAsync(
            "/v1/card-topups", new { walletId = _wallet, amount = 100m, currency = "TRY", returnUrl = ReturnUrl }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LimitIcinde_202_PayAyrilirOdemeAcilir()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await StartAsync(1_000m, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = body.GetProperty("cardTopupId").GetGuid();

        body.GetProperty("state").GetString().ShouldBe("pending");
        body.GetProperty("paymentUrl").GetString()!.ShouldStartWith(StripeFakeFactory.PublicUrl);
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        await using (var db = postgres.CreateContext())
        {
            var hold = await db.CardTopupHolds.SingleAsync(h => h.Id == id, ct);
            hold.AccountId.ShouldBe(_account);
            hold.Amount.ShouldBe(1_000m);
        }

        var payment = await _provider.CreateClient().GetAsync($"/v1/payments?reference={id}", ct);
        payment.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TopupAsync(id, ct)).AccountId.ShouldBe(_account);
    }

    [Fact]
    public async Task AyniAnahtar_AyniYuklemeDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        var first = await (await StartAsync(500m, key, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var second = await StartAsync(500m, key, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("cardTopupId").GetGuid().ShouldBe(first.GetProperty("cardTopupId").GetGuid());
        body.GetProperty("paymentUrl").GetString().ShouldBe(first.GetProperty("paymentUrl").GetString());
        body.GetProperty("replayed").GetBoolean().ShouldBeTrue();

        await using var db = postgres.CreateContext();
        (await db.CardTopupHolds.CountAsync(h => h.WalletId == _wallet, ct)).ShouldBe(1);
    }

    /// <summary>
    /// Ödeme sayfası müşteriyi dönüş adresine yüklemenin kimliğiyle yolluyor: arayüz hangi
    /// yüklemenin sonucunu soracağını oradan biliyor.
    /// </summary>
    [Fact]
    public async Task OdemeSayfasi_DonusAdresineYuklemeKimligiyle()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(100m, ct);
        var topup = await TopupAsync(id, ct);

        var decided = await _provider.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .PostAsync($"/odeme/{topup.PaymentId}", new FormUrlEncodedContent([new("action", "cancel")]), ct);

        decided.StatusCode.ShouldBe(HttpStatusCode.SeeOther);
        decided.Headers.Location.ShouldBe(new Uri($"{ReturnUrl}?cardTopupId={id}"));
    }

    /// <summary>Limit yetmiyor: ödeme hiç açılmıyor, wallet'ın reddi aynen dönüyor.</summary>
    [Fact]
    public async Task LimitYetmiyor_422_OdemeAcilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        var response = await StartAsync(6_000m, key, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString()
            .ShouldBe("card_topup_limit");

        // Tekrar: yükleme reddedilmiş olarak dönüyor, wallet'a yeniden sorulmuyor.
        var replay = await StartAsync(6_000m, key, ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("state").GetString().ShouldBe("rejected");
        body.GetProperty("failureReason").GetString().ShouldBe("card_topup_limit");

        var id = body.GetProperty("cardTopupId").GetGuid();
        var payment = await _provider.CreateClient().GetAsync($"/v1/payments?reference={id}", ct);
        payment.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = postgres.CreateContext();
        (await db.CardTopupHolds.AnyAsync(h => h.Id == id, ct)).ShouldBeFalse();
    }

    /// <summary>wallet-api'ye ulaşılamıyor: 503, yükleme açık kalıyor ve tekrar onu tamamlıyor.</summary>
    [Fact]
    public async Task WalletUlasilamaz_503_TekrarTamamlar()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        await using (var down = Service(wallet: false, provider: true))
        {
            var response = await StartAsync(down, _account, _wallet, 300m, key, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        var replay = await StartAsync(300m, key, ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("state").GetString().ShouldBe("pending");
        body.GetProperty("paymentUrl").GetString().ShouldNotBeNull();
        body.GetProperty("replayed").GetBoolean().ShouldBeTrue();
    }

    /// <summary>Sağlayıcıya ulaşılamıyor: pay ayrıldı, ödeme açılmadı; tekrar ödemeyi açıyor.</summary>
    [Fact]
    public async Task SaglayiciUlasilamaz_503_TekrarOdemeyiAcar()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        await using (var down = Service(wallet: true, provider: false))
        {
            var response = await StartAsync(down, _account, _wallet, 300m, key, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        var replay = await StartAsync(300m, key, ct);
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>(ct);

        body.GetProperty("state").GetString().ShouldBe("pending");
        body.GetProperty("paymentUrl").GetString()!.ShouldStartWith(StripeFakeFactory.PublicUrl);
    }

    // ------------------------------------------------------------------
    // Sağlayıcının bildirimi
    // ------------------------------------------------------------------
    /// <summary>Kart çekildi: kapanış outbox'ta, wallet onu işleyince para cüzdanda.</summary>
    [Fact]
    public async Task Odendi_KapanisWalletaGider_ParaCuzdanda()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(750m, ct);

        (await NotifyAsync(id, CardPaymentEvents.Succeeded, ct)).ShouldBe(TransitionResult.Applied);
        (await NotifyAsync(id, CardPaymentEvents.Succeeded, ct)).ShouldBe(TransitionResult.Ignored);

        var topup = await TopupAsync(id, ct);
        topup.State.ShouldBe(CardTopupState.Paid);
        topup.PaymentUrl.ShouldBeNull();

        var closure = (await ClosuresAsync(id, ct)).ShouldHaveSingleItem();
        closure.Outcome.ShouldBe(CardTopupClosedOutcomes.Paid);
        closure.ProviderRef.ShouldBe(topup.PaymentId);
        closure.Amount.ShouldBe(750m);

        var result = await new ProcessCardTopupHandler(
            postgres.ContextFactory,
            TestProviders.Policy,
            new SystemClock(),
            NullLogger<ProcessCardTopupHandler>.Instance).HandleAsync(closure, ct);

        result.Replayed.ShouldBeFalse();
        (await WalletBalanceAsync(ct)).ShouldBe(750m);
    }

    [Fact]
    public async Task Vazgecildi_OdenmediKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(200m, ct);

        (await NotifyAsync(id, CardPaymentEvents.Canceled, ct)).ShouldBe(TransitionResult.Applied);

        var topup = await TopupAsync(id, ct);
        topup.State.ShouldBe(CardTopupState.Failed);
        topup.FailureReason.ShouldBe(FailureReasons.Canceled);

        (await ClosuresAsync(id, ct)).ShouldHaveSingleItem().Outcome.ShouldBe(CardTopupClosedOutcomes.Failed);
    }

    /// <summary>Kaydımızla uyuşmayan bildirim: yükleme değişmiyor, kapanış gitmiyor.</summary>
    [Fact]
    public async Task TutarUyusmaz_Celiski()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(200m, ct);

        (await NotifyAsync(id, CardPaymentEvents.Succeeded, ct, amount: 250m)).ShouldBe(TransitionResult.Conflict);
        (await NotifyAsync(id, CardPaymentEvents.Succeeded, ct, paymentId: "pay_baska")).ShouldBe(TransitionResult.Conflict);

        (await TopupAsync(id, ct)).State.ShouldBe(CardTopupState.Pending);
        (await ClosuresAsync(id, ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task OdendiktenSonraVazgecildi_Celiski()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(200m, ct);

        await NotifyAsync(id, CardPaymentEvents.Succeeded, ct);

        (await NotifyAsync(id, CardPaymentEvents.Canceled, ct)).ShouldBe(TransitionResult.Conflict);
        (await TopupAsync(id, ct)).State.ShouldBe(CardTopupState.Paid);
    }

    /// <summary>Bizim açmadığımız ödemenin bildirimi: dead-letter'a gidecek.</summary>
    [Fact]
    public async Task BilinmeyenReferans_Bulunamaz()
    {
        var ct = TestContext.Current.CancellationToken;

        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ApplyCardPaymentHandler>();

        await Should.ThrowAsync<CardTopupNotFoundException>(() => handler.HandleAsync(new CardPaymentUpdated
        {
            Provider = "stripe-fake",
            EventId = "evt_yok",
            Type = CardPaymentEvents.Succeeded,
            PaymentId = "pay_yok",
            Reference = Guid.NewGuid(),
            Amount = 10m,
            Currency = "TRY",
            OccurredAt = DateTimeOffset.UtcNow
        }, ct));
    }

    // ------------------------------------------------------------------
    // Tarama
    // ------------------------------------------------------------------
    /// <summary>Oturumu kapanmış ödeme: sağlayıcı bildirmiyor, tarama sorup kapatıyor.</summary>
    [Fact]
    public async Task Tarama_OturumuDolmus_OdenmediKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(400m, ct);
        var later = (await TopupAsync(id, ct)).ExpiresAt + TimeSpan.FromMinutes(5);

        _providerTime.Now = later;

        var report = await Scanner(_factory, later).ScanAsync(ct);

        report.Closed.ShouldBeGreaterThanOrEqualTo(1);

        var topup = await TopupAsync(id, ct);
        topup.State.ShouldBe(CardTopupState.Failed);
        topup.FailureReason.ShouldBe(FailureReasons.Expired);

        (await ClosuresAsync(id, ct)).ShouldHaveSingleItem().Outcome.ShouldBe(CardTopupClosedOutcomes.Failed);
    }

    /// <summary>Bildirimi kaçırılmış ödeme: kart çekilmiş, tarama sağlayıcıdan öğrenip kapatıyor.</summary>
    [Fact]
    public async Task Tarama_KacirilanOdeme_OdendiKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(400m, ct);
        var topup = await TopupAsync(id, ct);

        // Sayfa müşteriyi dönüş adresine yolluyor; o adres bu test sunucusunda yok.
        var pay = await _provider.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .PostAsync($"/odeme/{topup.PaymentId}", new FormUrlEncodedContent([new("action", "pay")]), ct);
        pay.StatusCode.ShouldBe(HttpStatusCode.SeeOther);

        var later = topup.ExpiresAt + TimeSpan.FromMinutes(5);
        _providerTime.Now = later;

        await Scanner(_factory, later).ScanAsync(ct);

        (await TopupAsync(id, ct)).State.ShouldBe(CardTopupState.Paid);
        (await ClosuresAsync(id, ct)).ShouldHaveSingleItem().Outcome.ShouldBe(CardTopupClosedOutcomes.Paid);
    }

    /// <summary>Pay ayrıldı ama ödeme hiç açılmadı: oturum dolunca sağlayıcı tanımıyor, kapanıyor.</summary>
    [Fact]
    public async Task Tarama_OdemeAcilmamis_OdenmediKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        await using (var down = Service(wallet: true, provider: false))
        {
            (await StartAsync(down, _account, _wallet, 100m, key, ct)).StatusCode
                .ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        Topup topup;

        await using (var db = cards.CreateContext())
        {
            topup = await db.CardTopups.AsNoTracking().SingleAsync(c => c.IdempotencyKey == key, ct);
        }

        topup.State.ShouldBe(CardTopupState.Pending);

        var later = topup.ExpiresAt + TimeSpan.FromMinutes(5);
        _providerTime.Now = later;

        await Scanner(_factory, later).ScanAsync(ct);

        var closed = await TopupAsync(topup.Id, ct);
        closed.State.ShouldBe(CardTopupState.Failed);
        closed.FailureReason.ShouldBe(FailureReasons.PaymentNotOpened);
    }

    /// <summary>Pay isteği cevapsız kalmış yükleme: tarama terk edilmiş sayıp kapatıyor.</summary>
    [Fact]
    public async Task Tarama_TerkEdilmis_OdenmediKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Guid.NewGuid().ToString();

        await using (var down = Service(wallet: false, provider: true))
        {
            (await StartAsync(down, _account, _wallet, 100m, key, ct)).StatusCode
                .ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        Guid id;

        await using (var db = cards.CreateContext())
        {
            id = (await db.CardTopups.AsNoTracking().SingleAsync(c => c.IdempotencyKey == key, ct)).Id;
        }

        await Scanner(_factory, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(10)).ScanAsync(ct);

        var topup = await TopupAsync(id, ct);
        topup.State.ShouldBe(CardTopupState.Failed);
        topup.FailureReason.ShouldBe(FailureReasons.Abandoned);

        // Pay yazılmadı; wallet kapanışı zararsızca atlıyor.
        var closure = (await ClosuresAsync(id, ct)).ShouldHaveSingleItem();
        closure.Outcome.ShouldBe(CardTopupClosedOutcomes.Failed);
    }

    // ------------------------------------------------------------------
    // Görüntüleme
    // ------------------------------------------------------------------
    [Fact]
    public async Task Goruntuleme_Sahibi200_Baskasi404_Calisan200()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await PendingAsync(100m, ct);

        var own = await _factory.CreateClient().AsOwnerOf(_account).GetAsync($"/v1/card-topups/{id}", ct);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString().ShouldBe("pending");

        var other = await _factory.CreateClient().AsOwnerOf(Guid.NewGuid()).GetAsync($"/v1/card-topups/{id}", ct);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var staff = await _factory.CreateClient().AsStaff("calisan-1", "customer.view").GetAsync($"/v1/card-topups/{id}", ct);
        staff.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Çalışan müşteri yerine para yüklemiyor.</summary>
    [Fact]
    public async Task Calisan_Baslatamaz_403()
    {
        var ct = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/card-topups")
        {
            Content = JsonContent.Create(new { walletId = _wallet, amount = 100m, currency = "TRY", returnUrl = ReturnUrl })
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _factory.CreateClient().AsStaff("calisan-1", "customer.view").SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Transfers;

/// <summary>
/// HTTP sınırı: validation, Wolverine dispatch'i ve ProblemDetails eşlemesi.
/// Handler'ı doğrudan çağıran testler bu katmanların hiçbirini kapsamıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TransfersApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _from;
    private Guid _to;
    private Guid _business;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            var person = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var other = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var shop = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);

            _from = await LedgerSeeder.CreateWalletAsync(db, person, "API gönderen", ct);
            _to = await LedgerSeeder.CreateWalletAsync(db, other, "API alıcı", ct);
            _business = await LedgerSeeder.CreateWalletAsync(db, shop, "API dükkan", ct);

            await LedgerSeeder.FundAsync(db, _from, 1_000m, ct);
        }

        _factory = new WalletApiFactory(postgres);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<int> TransferCountAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.LedgerTransactions
            .Where(t => t.LedgerAccountId == _from)
            .CountAsync(ct);
    }

    /// <param name="omitKey">
    /// Yalnızca "anahtarsız istek reddediliyor mu" testi için. Varsayılan davranış
    /// anahtar ÜRETMEK: başlık zorunlu (decisions.md madde 4) ve diğer testlerin
    /// konusu idempotency değil.
    /// </param>
    private HttpRequestMessage Post(object body, string? idempotencyKey = null, bool omitKey = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(body)
        };

        if (!omitKey)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        }

        return request;
    }

    /// <summary>
    /// Anahtarsız istek <c>400</c> ile reddediliyor (<c>decisions.md</c> madde 4).
    ///
    /// Kabul edilseydi şu sessizce çift harcama üretirdi: ledger commit oldu, yanıt
    /// dönerken bağlantı koptu, istemci "oldu mu olmadı mı" bilmediği için tekrar
    /// denedi. Anahtarsız tekrar hiçbir constraint'e takılmaz — ikinci transfer
    /// yazılır ve hiçbir uyarı çıkmaz.
    /// </summary>
    [Fact]
    public async Task Post_AnahtarYok_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var before = await TransferCountAsync(ct);

        var response = await _client.SendAsync(
            Post(new { fromWalletId = _from, toWalletId = _to, amount = 10m, currency = "TRY", type = "P2P" },
                omitKey: true),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await TransferCountAsync(ct)).ShouldBe(before, "reddedilen istek ledger'a satır bırakmamalı");
    }

    [Fact]
    public async Task Post_GecerliTransfer_201Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _to,
            amount = 100m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        body.GetProperty("transactionId").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Post_AyniIdempotencyKey_AyniIslemiDoner()
    {
        var ct = TestContext.Current.CancellationToken;

        var payload = new
        {
            fromWalletId = _from,
            toWalletId = _to,
            amount = 25m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        };

        var first = await _client.SendAsync(Post(payload, "api-tekrar"), ct);
        var second = await _client.SendAsync(Post(payload, "api-tekrar"), ct);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);

        firstBody.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        secondBody.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        secondBody.GetProperty("transactionId").GetGuid()
            .ShouldBe(firstBody.GetProperty("transactionId").GetGuid());
    }

    [Fact]
    public async Task Post_YetersizBakiye_422DonerVeProblemDetailsOlur()
    {
        var ct = TestContext.Current.CancellationToken;

        // Cüzdanda 1.000 var, P2P işlem tavanı 10.000. Tavanın ALTINDA ama bakiyenin
        // ÜSTÜNDE bir tutar seçiliyor; aksi halde önce limit kuralı devreye girer.
        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _to,
            amount = 5_000m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("status").GetInt32().ShouldBe(422);
        problem.GetProperty("rule").GetString().ShouldBe("insufficient_funds");
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Post_LimitAsimi_422DonerVeKuralAdiniSoyler()
    {
        var ct = TestContext.Current.CancellationToken;

        // appsettings: P2P.PerTransaction = 10000
        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _to,
            amount = 10_001m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("rule").GetString().ShouldBe("P2P.PerTransaction");
    }

    [Fact]
    public async Task Post_OlmayanCuzdan_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = Guid.NewGuid(),
            toWalletId = _to,
            amount = 10m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0, "TRY", "tutar sıfır")]
    [InlineData(-5, "TRY", "tutar negatif")]
    [InlineData(10, "TL", "geçersiz para birimi kodu")]
    [InlineData(10.005, "TRY", "kuruşun altında tutar")]
    public async Task Post_GecersizGirdi_400Doner(decimal amount, string currency, string reason)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _to,
            amount,
            currency,
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, reason);
    }

    [Fact]
    public async Task Post_AyniCuzdanaTransfer_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _from,
            amount = 10m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_KomisyonluOdeme_GelirHesabinaYazar()
    {
        var ct = TestContext.Current.CancellationToken;

        // appsettings: Payment komisyonu %2
        var response = await _client.SendAsync(Post(new
        {
            fromWalletId = _from,
            toWalletId = _business,
            amount = 200m,
            currency = "TRY",
            type = nameof(TransferType.Payment)
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var transactionId = body.GetProperty("transactionId").GetGuid();

        await using var db = postgres.CreateContext();
        var entries = db.LedgerEntries.Where(e => e.TransactionId == transactionId).ToList();

        entries.Count.ShouldBe(3);
        entries.Single(e => e.LedgerAccountId == _from).Amount.ShouldBe(-204m);
        entries.Single(e => e.LedgerAccountId == _business).Amount.ShouldBe(200m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.RevenueTry).Amount.ShouldBe(4m);
    }
}

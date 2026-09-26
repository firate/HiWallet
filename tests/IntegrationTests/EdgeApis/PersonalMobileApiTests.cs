using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// personal-mobile-api, arkasında gerçek wallet-api ve withdrawal-orchestrator ile.
/// Üç host da HTTP üzerinden konuşuyor; ön API'nin veritabanı yok, veri iç
/// servislerin veritabanlarında kuruluyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PersonalMobileApiTests(PostgresFixture postgres, OrchestratorFixture orchestratorDb)
    : IAsyncLifetime
{
    private const string Iban = "TR330006100519786457841326";

    private WalletApiFactory _walletApi = null!;
    private WithdrawalOrchestratorApiFactory _orchestrator = null!;
    private PersonalMobileApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _walletApi = new WalletApiFactory(postgres);
        _orchestrator = new WithdrawalOrchestratorApiFactory(orchestratorDb);
        _factory = new PersonalMobileApiFactory(
            walletApi: new PassthroughHandler(_walletApi.CreateClient()),
            withdrawalOrchestrator: new PassthroughHandler(_orchestrator.CreateClient()));
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _orchestrator.DisposeAsync();
        await _walletApi.DisposeAsync();
    }

    private async Task<Guid> FundedWalletAsync(decimal amount, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, "Ana", ct);

        if (amount > 0)
        {
            await LedgerSeeder.FundAsync(db, walletId, amount, ct);
        }

        return walletId;
    }

    private Task<HttpResponseMessage> TransferAsync(
        Guid from, Guid to, decimal amount, string? idempotencyKey, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new
            {
                fromWalletId = from,
                toWalletId = to,
                amount,
                currency = "TRY",
                type = "P2P"
            })
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return _client.SendAsync(request, ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    [Fact]
    public async Task Cuzdan_WalletApidenGelir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await FundedWalletAsync(150m, ct);

        var response = await _client.GetAsync($"/v1/wallets/{walletId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", _walletApi.Errors));
        var body = await ReadAsync(response, ct);
        body.GetProperty("walletId").GetGuid().ShouldBe(walletId);
        body.GetProperty("balance").GetDecimal().ShouldBe(150m);
        body.GetProperty("balances").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Hareketler_SayfaParametreleriIletilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await FundedWalletAsync(10m, ct);

        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, walletId, 20m, ct);
        }

        var first = await ReadAsync(await _client.GetAsync($"/v1/wallets/{walletId}/movements?size=1", ct), ct);

        first.GetProperty("items").GetArrayLength().ShouldBe(1);
        var cursor = first.GetProperty("nextCursor").GetInt64();

        var second = await ReadAsync(
            await _client.GetAsync($"/v1/wallets/{walletId}/movements?size=1&after={cursor}", ct), ct);

        second.GetProperty("items")[0].GetProperty("amount").GetDecimal().ShouldBe(10m);
    }

    [Fact]
    public async Task Promolar_WalletApidenGelir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await FundedWalletAsync(0m, ct);

        var response = await _client.GetAsync($"/v1/wallets/{walletId}/promos", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response, ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Transfer_AyniAnahtarlaTekrar_YeniTransferYapmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var from = await FundedWalletAsync(100m, ct);
        var to = await FundedWalletAsync(0m, ct);
        var key = Guid.NewGuid().ToString();

        var first = await TransferAsync(from, to, 30m, key, ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _walletApi.Errors));
        var firstBody = await ReadAsync(first, ct);
        firstBody.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        var second = await TransferAsync(from, to, 30m, key, ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secondBody = await ReadAsync(second, ct);
        secondBody.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        secondBody.GetProperty("transactionId").GetGuid()
            .ShouldBe(firstBody.GetProperty("transactionId").GetGuid());

        var wallet = await ReadAsync(await _client.GetAsync($"/v1/wallets/{to}", ct), ct);
        wallet.GetProperty("balance").GetDecimal().ShouldBe(30m);
    }

    /// <summary>
    /// Anahtarı ön API aramıyor, wallet-api arıyor: kural tek yerde. Ön API'nin işi
    /// başlığı değiştirmeden taşımak ve reddi olduğu gibi geri vermek.
    /// </summary>
    [Fact]
    public async Task Transfer_AnahtarsizIstek_WalletApinin400uAynenDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var from = await FundedWalletAsync(100m, ct);
        var to = await FundedWalletAsync(0m, ct);

        var response = await TransferAsync(from, to, 30m, idempotencyKey: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response, ct)).GetProperty("errors").TryGetProperty("Idempotency-Key", out _)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Transfer_YetersizBakiye_422VeKuralAdiAynenDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var from = await FundedWalletAsync(10m, ct);
        var to = await FundedWalletAsync(0m, ct);

        var response = await TransferAsync(from, to, 500m, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadAsync(response, ct)).GetProperty("rule").GetString().ShouldBe("insufficient_funds");
    }

    [Fact]
    public async Task Cuzdan_Yok_404AynenDoner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>
    /// Location ön API'nin kendi adresini gösteriyor. İç servisin adresi dışarı
    /// sızsaydı istemci ulaşamayacağı bir bağlantı alırdı.
    /// </summary>
    [Fact]
    public async Task Cekim_202VeLocationOnApininAdresiniGosterir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = await FundedWalletAsync(500m, ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                walletId,
                amount = 100m,
                currency = "TRY",
                destinationIban = Iban
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, string.Join("\n", _walletApi.Errors));
        var withdrawalId = (await ReadAsync(response, ct)).GetProperty("withdrawalId").GetGuid();

        var location = response.Headers.Location.ShouldNotBeNull();
        location.Host.ShouldBe(_client.BaseAddress!.Host);
        location.AbsolutePath.ShouldBe($"/v1/withdrawals/{withdrawalId}");

        var followed = await _client.GetAsync(location, ct);
        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withdrawal = await ReadAsync(followed, ct);
        withdrawal.GetProperty("amount").GetDecimal().ShouldBe(100m);
        withdrawal.GetProperty("destinationIban").GetString().ShouldNotBe(Iban, "IBAN maskeli dönmeli");
    }

    [Fact]
    public async Task Cekim_CuzdanYok_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                walletId = Guid.NewGuid(),
                amount = 100m,
                currency = "TRY",
                destinationIban = Iban
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// İç servise ulaşılamadığında istemci ProblemDetails gövdeli <c>503</c> alıyor:
/// yeniden denenebilir bir durum ve bağlantı hatasının ayrıntısı dışarı çıkmıyor.
/// </summary>
public sealed class PersonalMobileApiUnavailableTests
{
    [Fact]
    public async Task WalletApiUlasilamiyor_503Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new PersonalMobileApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldNotContain("reddedildi", Case.Insensitive, "bağlantı hatasının metni dışarı çıkmamalı");
    }

    /// <summary>
    /// POST yeniden denenmiyor: tekrar istemcinin işi, aynı <c>Idempotency-Key</c> ile.
    /// Ön API de yeniden deneseydi zaman aşımına uğrayan her istek iç serviste katlanırdı.
    /// </summary>
    [Fact]
    public async Task Post_YenidenDenenmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletApi = new CountingHandler();

        await using var factory = new PersonalMobileApiFactory(walletApi: walletApi);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(new
            {
                fromWalletId = Guid.NewGuid(),
                toWalletId = Guid.NewGuid(),
                amount = 1m,
                currency = "TRY",
                type = "P2P"
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        walletApi.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Get_YenidenDenenir()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletApi = new CountingHandler();

        await using var factory = new PersonalMobileApiFactory(walletApi: walletApi);
        using var client = factory.CreateClient();

        await client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);

        walletApi.Calls.ShouldBeGreaterThan(1);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}

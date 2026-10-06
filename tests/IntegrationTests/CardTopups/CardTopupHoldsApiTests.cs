using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.CardTopups;

/// <summary>
/// wallet-api'nin kartla yükleme payı ucu: kart yüklemesi servisi müşterinin token'ını
/// iletip çağırıyor. HTTP sınırı: sahiplik, doğrulama ve limit reddinin kuralı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardTopupHoldsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
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

        _factory = new WalletApiFactory(postgres);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Owner() => _factory.CreateClient().AsOwnerOf(_account);

    private object Body(decimal amount, Guid? holdId = null, Guid? wallet = null) => new
    {
        holdId = holdId ?? Guid.NewGuid(),
        walletId = wallet ?? _wallet,
        amount,
        currency = "TRY",
        provider = "stripe-fake"
    };

    [Fact]
    public async Task LimitinAltinda_201_PayYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var holdId = Guid.NewGuid();

        var response = await Owner().PostAsJsonAsync("/v1/card-topup-holds", Body(1_000m, holdId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("holdId").GetGuid().ShouldBe(holdId);
        body.GetProperty("accountId").GetGuid().ShouldBe(_account);
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();

        await using var db = postgres.CreateContext();
        (await db.CardTopupHolds.AnyAsync(h => h.Id == holdId, ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task AyniKimlik_AyniPayiDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var holdId = Guid.NewGuid();

        await Owner().PostAsJsonAsync("/v1/card-topup-holds", Body(100m, holdId), ct);
        var second = await Owner().PostAsJsonAsync("/v1/card-topup-holds", Body(100m, holdId), ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("replayed").GetBoolean().ShouldBeTrue();
    }

    /// <summary>Limit yetmiyor: ödeme açılmayacak, müşteriye kendi yüklemesinin kuralı söyleniyor.</summary>
    [Fact]
    public async Task LimitiAsiyor_422_KuralAdi()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Owner().PostAsJsonAsync("/v1/card-topup-holds", Body(6_000m), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("rule").GetString().ShouldBe("card_topup_limit");
    }

    /// <summary>Başkasının cüzdanı yokmuş gibi: varlığı dışarı verilmiyor.</summary>
    [Fact]
    public async Task BaskasininCuzdani_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid other;

        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Unverified);
            other = await LedgerSeeder.CreateWalletAsync(db, account, "Başkası", ct);
        }

        var response = await Owner().PostAsJsonAsync("/v1/card-topup-holds", Body(100m, wallet: other), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GecersizGovde_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Owner().PostAsJsonAsync("/v1/card-topup-holds", new
        {
            holdId = Guid.Empty,
            walletId = _wallet,
            amount = -5m,
            currency = "try",
            provider = ""
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("HoldId", out _).ShouldBeTrue();
        errors.TryGetProperty("Amount", out _).ShouldBeTrue();
        errors.TryGetProperty("Currency", out _).ShouldBeTrue();
        errors.TryGetProperty("Provider", out _).ShouldBeTrue();
    }

    /// <summary>Çalışan müşteri yerine para yüklemiyor: uç çalışana kapalı.</summary>
    [Fact]
    public async Task Calisan_403()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _factory.CreateClient()
            .AsStaff("calisan-1", "customer.view")
            .PostAsJsonAsync("/v1/card-topup-holds", Body(100m), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

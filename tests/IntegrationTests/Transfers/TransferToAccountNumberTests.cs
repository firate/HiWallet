using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Transfers;

/// <summary>
/// Alıcı hesap numarasıyla seçiliyor; para alıcının o para birimindeki varsayılan
/// cüzdanına düşüyor. Çekirdek yine cüzdandan cüzdana: numara sınırda cüzdana çevriliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TransferToAccountNumberTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _sender = null!;
    private Guid _from;
    private Guid _recipient;
    private string _recipientNumber = null!;
    private Guid _recipientFirst;
    private Guid _recipientSecond;
    private string _walletlessNumber = null!;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            var sender = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _from = await LedgerSeeder.CreateWalletAsync(db, sender, "Gönderen", ct);
            await LedgerSeeder.FundAsync(db, _from, 1_000m, ct);

            _recipient = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _recipientFirst = await LedgerSeeder.CreateWalletAsync(db, _recipient, "Ana", ct);
            _recipientSecond = await LedgerSeeder.CreateWalletAsync(db, _recipient, "Birikim", ct);

            var walletless = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

            _recipientNumber = await NumberAsync(db, _recipient, ct);
            _walletlessNumber = await NumberAsync(db, walletless, ct);

            _factory = new WalletApiFactory(postgres);
            _sender = _factory.CreateClient().AsOwnerOf(sender);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sender.Dispose();
        await _factory.DisposeAsync();
    }

    private static async Task<string> NumberAsync(WalletDbContext db, Guid accountId, CancellationToken ct) =>
        (await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Number).SingleAsync(ct)).Value;

    private Task<HttpResponseMessage> TransferAsync(object body, CancellationToken ct) =>
        _sender.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/v1/transfers")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString("N") } }
        }, ct);

    private object ToNumber(string number, decimal amount = 10m) => new
    {
        fromWalletId = _from,
        toAccountNumber = number,
        amount,
        currency = "TRY",
        type = nameof(TransferType.P2P)
    };

    private async Task<decimal> BalanceAsync(Guid walletId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await LedgerSeeder.BalanceAsync(db, walletId, ct);
    }

    [Fact]
    public async Task HesapNumarasina_VarsayilanCuzdanaGider()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await TransferAsync(ToNumber(_recipientNumber, 25m), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        (await BalanceAsync(_recipientFirst, ct)).ShouldBe(25m);
        (await BalanceAsync(_recipientSecond, ct)).ShouldBe(0m);
    }

    /// <summary>Varsayılan değişince para yenisine gidiyor; numaradaki gruplama boşlukları kabul ediliyor.</summary>
    [Fact]
    public async Task VarsayilanDegisince_YeniCuzdanaGider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var recipient = _factory.CreateClient().AsOwnerOf(_recipient);
        (await recipient.PutAsJsonAsync($"/v1/accounts/{_recipient}/default-wallets/TRY", new { walletId = _recipientSecond }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var grouped = $"{_recipientNumber[..3]} {_recipientNumber[3..6]} {_recipientNumber[6..]}";
        var response = await TransferAsync(ToNumber(grouped, 7m), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        (await BalanceAsync(_recipientSecond, ct)).ShouldBe(7m);
    }

    /// <summary>Alıcı bu para biriminde ödeme alamıyor; hesabında kendiliğinden cüzdan açılmıyor.</summary>
    [Fact]
    public async Task AlicininBuParaBirimindeCuzdaniYok_422()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await TransferAsync(ToNumber(_walletlessNumber), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString()
            .ShouldBe("no_wallet_in_currency");
    }

    [Fact]
    public async Task OlmayanNumara404_KontrolHanesiTutmayan400()
    {
        var ct = TestContext.Current.CancellationToken;

        (await TransferAsync(ToNumber(AccountNumber.New().Value), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransferAsync(ToNumber("1234567890"), ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Alıcı ya cüzdanla ya hesap numarasıyla, ikisi birden değil.</summary>
    [Fact]
    public async Task AliciIkiKezYaDaHicVerilmez_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var both = new
        {
            fromWalletId = _from,
            toWalletId = _recipientFirst,
            toAccountNumber = _recipientNumber,
            amount = 10m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        };
        var neither = new { fromWalletId = _from, amount = 10m, currency = "TRY", type = nameof(TransferType.P2P) };

        (await TransferAsync(both, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransferAsync(neither, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

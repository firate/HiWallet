using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;

namespace HiWallet.IntegrationTests.Accounts;

/// <summary>
/// Hesap ve cüzdan açma uçları. Bunlardan önce cüzdan yalnızca doğrudan DB'ye
/// yazarak kuruluyordu — yani compose'dan koşan sistemde hiç kurulamıyordu.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> OpenAccountAsync(AccountType type, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/accounts", new { type = type.ToString() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("accountId").GetGuid();
    }

    private async Task<Guid> OpenWalletAsync(Guid accountId, string name, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(
            $"/v1/accounts/{accountId}/wallets", new { name, currency = "TRY" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("walletId").GetGuid();
    }

    [Fact]
    public async Task Post_Hesap_201DonerVeLocationOkunabilir()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/v1/accounts", new { type = nameof(AccountType.Business) }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("accountId").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("type").GetString().ShouldBe(nameof(AccountType.Business));

        // Location ölü bir bağlantı olmasın: gerçekten GET edilebilmeli.
        var location = response.Headers.Location.ShouldNotBeNull();
        var followed = await _client.GetAsync(location, ct);
        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_Cuzdan_SifirBakiyeIle201Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await OpenAccountAsync(AccountType.Person, ct);

        var response = await _client.PostAsJsonAsync(
            $"/v1/accounts/{accountId}/wallets", new { name = "Birikim", currency = "TRY" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        body.GetProperty("name").GetString().ShouldBe("Birikim");
        body.GetProperty("currency").GetString().ShouldBe("TRY");
        body.GetProperty("balance").GetDecimal().ShouldBe(0m);
    }

    /// <summary>
    /// Asıl kanıt: uçtan açılan cüzdan ledger'da GERÇEKTEN kullanılabiliyor mu.
    /// Bakiye satırı açılmasaydı cüzdan yaratılırdı ama ilk transfer
    /// "Bakiye satırı yok" ile 500 verirdi — sessiz ve geç ortaya çıkan kusur.
    /// </summary>
    [Fact]
    public async Task AcilanCuzdan_TransferdeKullanilabiliyor()
    {
        var ct = TestContext.Current.CancellationToken;

        var senderAccount = await OpenAccountAsync(AccountType.Person, ct);
        var receiverAccount = await OpenAccountAsync(AccountType.Person, ct);

        var sender = await OpenWalletAsync(senderAccount, "Gönderen", ct);
        var receiver = await OpenWalletAsync(receiverAccount, "Alıcı", ct);

        // Para ledger üzerinden giriyor; bakiyeye doğrudan yazmak zero-sum'ı bozardı.
        await using (var db = postgres.CreateContext())
        {
            await LedgerSeeder.FundAsync(db, sender, 500m, ct);
        }

        var transfer = await _client.PostAsJsonAsync("/v1/transfers", new
        {
            fromWalletId = sender,
            toWalletId = receiver,
            amount = 100m,
            currency = "TRY",
            type = nameof(TransferType.P2P)
        }, ct);

        transfer.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));

        var received = await _client.GetFromJsonAsync<JsonElement>($"/v1/wallets/{receiver}", ct);
        received.GetProperty("balance").GetDecimal().ShouldBe(100m);
    }

    [Fact]
    public async Task Post_Cuzdan_OlmayanHesap_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            $"/v1/accounts/{Guid.NewGuid()}/wallets", new { name = "Hayalet", currency = "TRY" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    /// <summary>
    /// ISO 4217'ye uygun ama sistem hesapları açılmamış bir para birimi. Girdi kuralı
    /// değil — o yüzden 400 değil 422, ve sınırdaki validator bunu yakalayamaz.
    /// </summary>
    [Fact]
    public async Task Post_Cuzdan_SistemHesabiOlmayanParaBirimi_422Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await OpenAccountAsync(AccountType.Person, ct);

        var response = await _client.PostAsJsonAsync(
            $"/v1/accounts/{accountId}/wallets", new { name = "Dolar", currency = "USD" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("rule").GetString().ShouldBe("unsupported_currency");
    }

    [Fact]
    public async Task Post_Cuzdan_AdiBos_400Doner()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await OpenAccountAsync(AccountType.Person, ct);

        var response = await _client.PostAsJsonAsync(
            $"/v1/accounts/{accountId}/wallets", new { name = "", currency = "TRY" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_Hesap_CuzdanlariBakiyeleriyleListeler()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = await OpenAccountAsync(AccountType.Person, ct);

        // Aynı hesabın aynı para biriminde iki cüzdanı — decisions.md madde 20'nin
        // izin verdiği durum; ad dışında ayırt edilemiyorlar.
        await OpenWalletAsync(accountId, "Birikim", ct);
        await OpenWalletAsync(accountId, "Harcama", ct);

        var body = await _client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct);

        var wallets = body.GetProperty("wallets").EnumerateArray().ToArray();
        wallets.Length.ShouldBe(2);
        wallets.Select(w => w.GetProperty("name").GetString()).ShouldBe(["Birikim", "Harcama"]);
        wallets.ShouldAllBe(w => w.GetProperty("balance").GetDecimal() == 0m);
    }

    [Fact]
    public async Task Get_OlmayanCuzdan_404Doner()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/v1/wallets/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Sistem hesabı bu uçtan görünmemeli: clearing ve revenue bakiyeleri iç muhasebe.
    /// Aynı tabloda durdukları için (decisions.md madde 20) filtre olmasa sızarlardı.
    /// </summary>
    [Fact]
    public async Task Get_SistemHesabi_CuzdanUcundanGorunmez()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/v1/wallets/{SystemAccounts.RevenueTry}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

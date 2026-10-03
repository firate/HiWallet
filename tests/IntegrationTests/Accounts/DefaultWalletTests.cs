using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.IntegrationTests.Accounts;

/// <summary>
/// Hesap numarasına gelen para o para birimindeki varsayılan cüzdana düşüyor. Her para
/// biriminde her zaman tam bir varsayılan var: ilk cüzdan kendiliğinden varsayılan, müşteri
/// onu yalnızca kendi cüzdanları arasında değiştiriyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DefaultWalletTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;
    private HttpClient _owner = null!;
    private Guid _accountId;
    private Guid _first;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory = new WalletApiFactory(postgres);

        var holder = $"kayit-{Guid.NewGuid():N}";
        using var onboarding = _factory.CreateClient().AsOnboarding();
        var opened = await (await onboarding.PostAsJsonAsync("/v1/person-accounts", new { holder }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        _accountId = opened.GetProperty("accountId").GetGuid();
        _first = opened.GetProperty("walletId").GetGuid();
        _owner = _factory.CreateClient().As(holder);
    }

    public async ValueTask DisposeAsync()
    {
        _owner.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> OpenWalletAsync(HttpClient client, Guid accountId, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync($"/v1/accounts/{accountId}/wallets", new { name, currency = "TRY" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("walletId").GetGuid();
    }

    private async Task<Dictionary<Guid, bool>> DefaultsAsync(HttpClient client, Guid accountId, CancellationToken ct) =>
        (await client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct))
        .GetProperty("wallets").EnumerateArray()
        .ToDictionary(w => w.GetProperty("walletId").GetGuid(), w => w.GetProperty("isDefault").GetBoolean());

    private Task<HttpResponseMessage> SetDefaultAsync(HttpClient client, Guid accountId, string currency, Guid walletId, CancellationToken ct) =>
        client.PutAsJsonAsync($"/v1/accounts/{accountId}/default-wallets/{currency}", new { walletId }, ct);

    [Fact]
    public async Task IlkCuzdanVarsayilan_IkincisiDegil()
    {
        var ct = TestContext.Current.CancellationToken;

        var second = await OpenWalletAsync(_owner, _accountId, "Birikim", ct);

        var defaults = await DefaultsAsync(_owner, _accountId, ct);
        defaults[_first].ShouldBeTrue();
        defaults[second].ShouldBeFalse();
    }

    /// <summary>İşyeri hesabı cüzdansız açılıyor; ilk cüzdanı varsayılan oluyor.</summary>
    [Fact]
    public async Task IsyerininIlkCuzdani_Varsayilan()
    {
        var ct = TestContext.Current.CancellationToken;
        using var merchant = _factory.CreateClient().As($"isyeri-{Guid.NewGuid():N}");
        var account = (await (await merchant.PostAsJsonAsync("/v1/accounts", new { type = nameof(AccountType.Business) }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accountId").GetGuid();

        var wallet = await OpenWalletAsync(merchant, account, "Kasa", ct);

        (await DefaultsAsync(merchant, account, ct))[wallet].ShouldBeTrue();
    }

    [Fact]
    public async Task VarsayilanDegisir_EskisiVarsayilanOlmaktanCikar()
    {
        var ct = TestContext.Current.CancellationToken;
        var second = await OpenWalletAsync(_owner, _accountId, "Birikim", ct);

        (await SetDefaultAsync(_owner, _accountId, "TRY", second, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var defaults = await DefaultsAsync(_owner, _accountId, ct);
        defaults[second].ShouldBeTrue();
        defaults[_first].ShouldBeFalse();
    }

    /// <summary>Başkasının cüzdanı varsayılan yapılamıyor; var olduğu da söylenmiyor.</summary>
    [Fact]
    public async Task BaskaHesabinCuzdani_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid foreign;
        await using (var db = postgres.CreateContext())
        {
            var other = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            foreign = await LedgerSeeder.CreateWalletAsync(db, other, "Başkası", ct);
        }

        (await SetDefaultAsync(_owner, _accountId, "TRY", foreign, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await DefaultsAsync(_owner, _accountId, ct))[_first].ShouldBeTrue();
    }

    [Fact]
    public async Task ParaBirimiUyusmuyor_422()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await SetDefaultAsync(_owner, _accountId, "EUR", _first, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Varsayılan cüzdan müşterinin tercihi: başkası ve çalışan değiştiremiyor.</summary>
    [Fact]
    public async Task BaskasiVeCalisanDegistiremez()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stranger = _factory.CreateClient().As($"yabanci-{Guid.NewGuid():N}");
        using var staff = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);

        (await SetDefaultAsync(stranger, _accountId, "TRY", _first, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SetDefaultAsync(staff, _accountId, "TRY", _first, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

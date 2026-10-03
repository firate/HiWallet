using System.Net;
using System.Net.Http.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.IntegrationTests.Authentication;

/// <summary>
/// Çalışanın izni token'da değil: wallet-api her istekte personel yönetimine soruyor,
/// çalışanın kendi token'ıyla. Arkada gerçek staff-admin ve veritabanı. Rolü alınan ya da
/// kapatılan çalışanın aynı token'la bir sonraki isteği reddediliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaffPermissionTests(PostgresFixture postgres, StaffAdminFixture staffDb) : IAsyncLifetime
{
    private StaffAdminApiFactory _staffAdmin = null!;
    private WalletApiFactory _walletApi = null!;
    private HttpClient _admin = null!;
    private Guid _walletId;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            _walletId = await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        }

        _staffAdmin = new StaffAdminApiFactory(staffDb);
        _admin = await _staffAdmin.CreateReadyClientAsync(ct);
        _admin.AsStaff((await _staffAdmin.AddAdminAsync(ct)).ToString());

        _walletApi = new WalletApiFactory(postgres, staffAdmin: new PassthroughHandler(_staffAdmin.CreateClient()));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _walletApi.DisposeAsync();
        await _staffAdmin.DisposeAsync();
    }

    /// <summary>Destek rolüyle bir çalışan; personel yönetiminde kayıtlı.</summary>
    private async Task<(Guid StaffId, HttpClient Wallet)> SupportStaffAsync(CancellationToken ct)
    {
        var role = $"Destek {Guid.NewGuid():N}"[..20];
        (await _admin.PostAsJsonAsync("/v1/roles", new { name = role, permissions = new[] { StaffPermissions.CustomerView } }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var staffId = await _staffAdmin.AddStaffAsync(ct, role);

        return (staffId, _walletApi.CreateClient().AsStaff(staffId.ToString()));
    }

    [Fact]
    public async Task RolAlininca_AyniTokenlaBirSonrakiIstek403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (staffId, wallet) = await SupportStaffAsync(ct);

        (await wallet.GetAsync($"/v1/wallets/{_walletId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PutAsJsonAsync($"/v1/staff/{staffId}/roles", new { roleIds = Array.Empty<Guid>() }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await wallet.GetAsync($"/v1/wallets/{_walletId}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kapatilinca_AyniTokenlaBirSonrakiIstek403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (staffId, wallet) = await SupportStaffAsync(ct);
        (await wallet.GetAsync($"/v1/wallets/{_walletId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PostAsync($"/v1/staff/{staffId}/disable", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await wallet.GetAsync($"/v1/wallets/{_walletId}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>İzni doğrulanamayan çalışan işlem yapamıyor; müşteri etkilenmiyor.</summary>
    [Fact]
    public async Task PersonelYonetimineUlasilamazsa_Calisan503_MusteriEtkilenmez()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var walletApi = new WalletApiFactory(postgres, staffAdmin: new UnreachableHandler());
        using var staff = walletApi.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}");
        using var anonymousCustomer = walletApi.CreateClient().As($"musteri-{Guid.NewGuid():N}");

        (await staff.GetAsync($"/v1/wallets/{_walletId}", ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await anonymousCustomer.GetAsync("/v1/accounts", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Personel yönetimi kapalı.");
    }
}

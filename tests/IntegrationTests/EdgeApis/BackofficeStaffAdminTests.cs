using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.IntegrationTests.EdgeApis;

/// <summary>
/// Personel yönetimi panelden: backoffice-bff arkasında gerçek staff-admin. Çalışanın
/// token'ı oturumdan iç servise gidiyor; izni ve kuralları iç servis kontrol ediyor, BFF
/// reddi aynen aktarıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BackofficeStaffAdminTests(StaffAdminFixture staffDb) : IAsyncLifetime
{
    private StaffAdminApiFactory _staffAdmin = null!;
    private BackofficeBffFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _staffAdmin = new StaffAdminApiFactory(staffDb);
        var internalClient = await _staffAdmin.CreateReadyClientAsync(TestContext.Current.CancellationToken);
        _factory = new BackofficeBffFactory(staffAdmin: new PassthroughHandler(internalClient));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _staffAdmin.DisposeAsync();
    }

    private HttpClient SignedIn(Guid staff, params string[] permissions) =>
        _factory.CreateClient().SignedInAs(staff.ToString(), permissions).WithCsrfHeader();

    [Fact]
    public async Task Yonetici_RolAcar_CalisaniDavetEder_KayittaGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = Guid.NewGuid();
        using var client = SignedIn(admin, StaffPermissions.StaffManage);

        var role = await client.PostAsJsonAsync("/v1/roles", new
        {
            name = $"Operasyon {Guid.NewGuid():N}"[..20],
            permissions = new[] { StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview }
        }, ct);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(ct));
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("roleId").GetGuid();

        var invited = await client.PostAsJsonAsync("/v1/staff", new
        {
            email = $"yeni-{Guid.NewGuid():N}@ornek.com",
            roleIds = new[] { roleId }
        }, ct);
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        var staffId = (await invited.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("staffId").GetGuid();
        invited.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe($"/v1/staff/{staffId}");

        var events = await client.GetFromJsonAsync<JsonElement>("/v1/audit-events?size=10", ct);
        events.GetProperty("items").EnumerateArray().ShouldContain(e =>
            e.GetProperty("targetId").GetGuid() == staffId
            && e.GetProperty("action").GetString() == "staff_invited"
            && e.GetProperty("actorSubject").GetString() == admin.ToString());
    }

    /// <summary>Panelin geri kalanını gören çalışan personel yönetimini göremiyor; reddi iç servis veriyor.</summary>
    [Fact]
    public async Task PersonelYonetimiIzniYok_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = SignedIn(Guid.NewGuid(), StaffPermissions.CustomerView);

        (await client.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/v1/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KendiRolleri_KuralAdiylaReddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = Guid.NewGuid();
        using var client = SignedIn(admin, StaffPermissions.StaffManage);

        var response = await client.PutAsJsonAsync($"/v1/staff/{admin}/roles", new { roleIds = Array.Empty<Guid>() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("own_account");
    }
}

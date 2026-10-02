using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.StaffAdmin;

/// <summary>
/// Personel yönetimi: izinler kodda, roller ve çalışanlar panelden. Kimlik sağlayıcının
/// yerinde sahte dizin, veritabanı gerçek. Her değişiklik işi yapan çalışanla kayıt altında;
/// çalışan kendine yetki veremiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StaffAdminApiTests(StaffAdminFixture staffDb) : IAsyncLifetime
{
    private StaffAdminApiFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _adminId;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory = new StaffAdminApiFactory(staffDb);
        _admin = await _factory.CreateReadyClientAsync(ct);

        // Testin yöneticisi: dizinde bir çalışan ve token'ında personel yönetimi izni.
        _adminId = _factory.Directory.AddUser($"yonetici-{Guid.NewGuid():N}@ornek.com");
        _admin.AsStaff(_adminId.ToString(), StaffPermissions.StaffManage);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    private static string NewRoleName() => $"Rol {Guid.NewGuid():N}"[..20];

    private async Task<Guid> CreateRoleAsync(string name, CancellationToken ct, params string[] permissions)
    {
        var response = await _admin.PostAsJsonAsync("/v1/roles", new { name, description = "Test", permissions }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("roleId").GetGuid();
    }

    private async Task<List<StaffAuditEvent>> EventsForAsync(Guid targetId, CancellationToken ct)
    {
        await using var db = staffDb.CreateContext();
        return await db.AuditEvents.Where(e => e.TargetId == targetId).OrderBy(e => e.Id).ToListAsync(ct);
    }

    // --- Kurulum ---

    /// <summary>
    /// Açılışta kodun izinleri dizine yazılıyor, yönetici rolü açılıyor ve kimsede personel
    /// yönetimi yoksa ilk yöneticiye davet gidiyor. Kimlik sağlayıcının konsoluna girilmiyor.
    /// </summary>
    [Fact]
    public async Task Kurulum_IzinleriYoneticiRolunuVeIlkYoneticiyiKurar()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ilk-{Guid.NewGuid():N}@ornek.com";
        await using var factory = new StaffAdminApiFactory(staffDb, email);
        using var _ = await factory.CreateReadyClientAsync(ct);

        foreach (var permission in StaffPermissions.All)
        {
            factory.Directory.RoleNamed(permission).ShouldNotBeNull(permission);
        }

        var adminRole = factory.Directory.RoleNamed(StaffAdminApiFactory.AdminRoleName).ShouldNotBeNull();
        adminRole.Permissions.ShouldBe([StaffPermissions.StaffManage]);

        var admin = factory.Directory.UserWithEmail(email).ShouldNotBeNull();
        factory.Directory.RolesOf(admin.Id).ShouldBe([adminRole.Id]);
        factory.Directory.Invitations.ShouldBe([admin.Id]);

        var events = await EventsForAsync(admin.Id, ct);
        events.ShouldContain(e => e.Action == StaffAuditAction.StaffInvited && e.ActorSubject == Actor.System.Subject);
    }

    // --- İzinler ve roller ---

    [Fact]
    public async Task Izinler_KoddakiListe()
    {
        var ct = TestContext.Current.CancellationToken;

        var permissions = await _admin.GetFromJsonAsync<JsonElement[]>("/v1/permissions", ct);

        permissions.ShouldNotBeNull().Select(p => p.GetProperty("name").GetString()).ShouldBe(StaffPermissions.All);
        permissions.ShouldAllBe(p => p.GetProperty("description").GetString()!.Length > 0);
    }

    [Fact]
    public async Task Rol_AcilirDegisirSilinir_HerAdimKayitta()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();

        var roleId = await CreateRoleAsync(name, ct, StaffPermissions.CustomerView);

        var list = await _admin.GetFromJsonAsync<JsonElement>("/v1/roles?size=100", ct);
        list.GetProperty("items").EnumerateArray().ShouldContain(r => r.GetProperty("roleId").GetGuid() == roleId);
        list.GetProperty("items").EnumerateArray()
            .ShouldNotContain(r => r.GetProperty("name").GetString() == _factory.Directory.DefaultRole.Name);

        var updated = await _admin.PutAsJsonAsync($"/v1/roles/{roleId}", new
        {
            description = "Operasyon ekibi",
            permissions = new[] { StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview }
        }, ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()).ShouldBe([StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview], ignoreOrder: true);

        (await _admin.DeleteAsync($"/v1/roles/{roleId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _admin.GetAsync($"/v1/roles/{roleId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var events = await EventsForAsync(roleId, ct);
        events.Select(e => e.Action).ShouldBe(
            [StaffAuditAction.RoleCreated, StaffAuditAction.RoleUpdated, StaffAuditAction.RoleDeleted]);
        events.ShouldAllBe(e => e.ActorSubject == _adminId.ToString() && e.TargetLabel == name);
        events[1].Details.ShouldContain(StaffPermissions.WithdrawalReview);
    }

    /// <summary>Kimlik sağlayıcının kendi rolünün adı alınamıyor: token'da ayırt edilemezdi.</summary>
    [Fact]
    public async Task Rol_KimlikSaglayicininAdi_422()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _admin.PostAsJsonAsync("/v1/roles", new
        {
            name = "offline_access",
            permissions = new[] { StaffPermissions.CustomerView }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString()
            .ShouldBe("role_name_reserved");
    }

    [Fact]
    public async Task Rol_BilinmeyenIzinYaDaIzinsiz_400()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _admin.PostAsJsonAsync("/v1/roles", new { name = NewRoleName(), permissions = new[] { "ledger.write" } }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _admin.PostAsJsonAsync("/v1/roles", new { name = NewRoleName(), permissions = Array.Empty<string>() }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rol_AyniAdBuyukKucukHarfFarkiyla_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        await CreateRoleAsync(name, ct, StaffPermissions.CustomerView);

        var response = await _admin.PostAsJsonAsync("/v1/roles", new
        {
            name = name.ToUpperInvariant(),
            permissions = new[] { StaffPermissions.CustomerView }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>Sahip olduğu rolü değiştiren çalışan kendine yetki vermiş olurdu.</summary>
    [Fact]
    public async Task KendiRolunuDegistiremezSilemez_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var roleId = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);
        await _factory.Directory.AssignRolesAsync(_adminId, [(await _factory.Directory.FindRoleAsync(roleId, ct))!], ct);

        var update = await _admin.PutAsJsonAsync($"/v1/roles/{roleId}", new
        {
            permissions = new[] { StaffPermissions.CustomerView, StaffPermissions.PromoGrant }
        }, ct);
        var delete = await _admin.DeleteAsync($"/v1/roles/{roleId}", ct);

        update.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await update.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("own_role");
        delete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _factory.Directory.FindRoleAsync(roleId, ct)).ShouldNotBeNull().Permissions.ShouldBe([StaffPermissions.CustomerView]);
    }

    // --- Çalışanlar ---

    [Fact]
    public async Task Davet_KullaniciyiAcarRolVerirDavetGonderir()
    {
        var ct = TestContext.Current.CancellationToken;
        var roleId = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);
        var email = $"yeni-{Guid.NewGuid():N}@ornek.com";

        var response = await _admin.PostAsJsonAsync("/v1/staff", new
        {
            email,
            firstName = "Ayşe",
            lastName = "Yılmaz",
            roleIds = new[] { roleId }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        var staff = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var staffId = staff.GetProperty("staffId").GetGuid();
        staff.GetProperty("invitationPending").GetBoolean().ShouldBeTrue();
        staff.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("roleId").GetGuid()).ShouldBe([roleId]);

        _factory.Directory.Invitations.ShouldContain(staffId);
        var events = await EventsForAsync(staffId, ct);
        events.ShouldHaveSingleItem().Action.ShouldBe(StaffAuditAction.StaffInvited);
        events[0].TargetLabel.ShouldBe(email);
    }

    [Fact]
    public async Task Davet_KayitliEposta_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"var-{Guid.NewGuid():N}@ornek.com";
        _factory.Directory.AddUser(email);

        var response = await _admin.PostAsJsonAsync("/v1/staff", new { email, roleIds = Array.Empty<Guid>() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>İzin ya da kimlik sağlayıcının rolü çalışana doğrudan verilemiyor; yalnızca panelin rolleri.</summary>
    [Fact]
    public async Task Davet_PanelinRoluOlmayan_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var permission = _factory.Directory.RoleNamed(StaffPermissions.StaffManage).ShouldNotBeNull();

        var response = await _admin.PostAsJsonAsync("/v1/staff", new
        {
            email = $"x-{Guid.NewGuid():N}@ornek.com",
            roleIds = new[] { permission.Id }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("unknown_role");
    }

    [Fact]
    public async Task Rolleri_Degisir_OncesiVeSonrasiKayitta()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);
        var second = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.WithdrawalReview);
        var staffId = _factory.Directory.AddUser($"c-{Guid.NewGuid():N}@ornek.com");
        await _factory.Directory.AssignRolesAsync(staffId, [(await _factory.Directory.FindRoleAsync(first, ct))!], ct);

        var response = await _admin.PutAsJsonAsync($"/v1/staff/{staffId}/roles", new { roleIds = new[] { second } }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Directory.RolesOf(staffId).ShouldBe([second]);
        var change = (await EventsForAsync(staffId, ct)).ShouldHaveSingleItem();
        change.Action.ShouldBe(StaffAuditAction.StaffRolesChanged);
        var details = JsonDocument.Parse(change.Details).RootElement;
        details.GetProperty("before").EnumerateArray().Select(r => r.GetString()).ShouldHaveSingleItem();
        details.GetProperty("after").EnumerateArray().Select(r => r.GetString()).ShouldHaveSingleItem();
    }

    /// <summary>Çalışan kendi rollerini değiştiremiyor ve kendini kapatamıyor.</summary>
    [Fact]
    public async Task KendiHesabi_RolDegistiremez_Kapatamaz_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var roleId = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.PromoGrant);

        var roles = await _admin.PutAsJsonAsync($"/v1/staff/{_adminId}/roles", new { roleIds = new[] { roleId } }, ct);
        var disable = await _admin.PostAsync($"/v1/staff/{_adminId}/disable", null, ct);

        roles.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await roles.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("own_account");
        disable.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        _factory.Directory.RolesOf(_adminId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Kapatilir_OturumuKapanir_YenidenAcilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var staffId = _factory.Directory.AddUser($"k-{Guid.NewGuid():N}@ornek.com");

        var disabled = await _admin.PostAsync($"/v1/staff/{staffId}/disable", null, ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await disabled.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
        _factory.Directory.SignedOut.ShouldContain(staffId);

        var enabled = await _admin.PostAsync($"/v1/staff/{staffId}/enable", null, ct);
        (await enabled.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("enabled").GetBoolean().ShouldBeTrue();

        (await EventsForAsync(staffId, ct)).Select(e => e.Action)
            .ShouldBe([StaffAuditAction.StaffDisabled, StaffAuditAction.StaffEnabled]);
    }

    [Fact]
    public async Task Davet_YenidenGonderilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var staffId = _factory.Directory.AddUser($"d-{Guid.NewGuid():N}@ornek.com");

        var response = await _admin.PostAsync($"/v1/staff/{staffId}/invitation", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Directory.Invitations.ShouldContain(staffId);
        (await EventsForAsync(staffId, ct)).ShouldHaveSingleItem().Action.ShouldBe(StaffAuditAction.InvitationSent);
    }

    [Fact]
    public async Task Personel_SayfaliVeAranir()
    {
        var ct = TestContext.Current.CancellationToken;
        var tag = Guid.NewGuid().ToString("N")[..8];
        foreach (var i in Enumerable.Range(1, 3))
        {
            _factory.Directory.AddUser($"ara{tag}-{i}@ornek.com");
        }

        var first = await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff?search=ara{tag}&size=2", ct);
        first.GetProperty("items").GetArrayLength().ShouldBe(2);
        var next = first.GetProperty("nextFirst").GetInt32();

        var second = await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff?search=ara{tag}&size=2&first={next}", ct);
        second.GetProperty("items").GetArrayLength().ShouldBe(1);
        second.GetProperty("nextFirst").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // --- Kayıtlar ve yetki ---

    [Fact]
    public async Task Kayitlar_YenidenEskiyeSayfali()
    {
        var ct = TestContext.Current.CancellationToken;
        await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);
        await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);

        var page = await _admin.GetFromJsonAsync<JsonElement>("/v1/audit-events?size=1", ct);
        var cursor = page.GetProperty("nextCursor").GetGuid();
        var next = await _admin.GetFromJsonAsync<JsonElement>($"/v1/audit-events?size=1&after={cursor}", ct);

        var newest = page.GetProperty("items")[0];
        var older = next.GetProperty("items")[0];
        newest.GetProperty("eventId").GetGuid().ShouldNotBe(older.GetProperty("eventId").GetGuid());
        newest.GetProperty("occurredAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(older.GetProperty("occurredAt").GetDateTimeOffset());
        newest.GetProperty("action").GetString().ShouldBe("role_created");
    }

    /// <summary>
    /// Yalnızca çalışanların kimlik sağlayıcısının token'ı ve personel yönetimi izniyle.
    /// Müşterinin token'ını bu servis tanımıyor.
    /// </summary>
    [Fact]
    public async Task Yetki_MusteriTanimiyor_IzinsizCalisan403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var customer = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");
        using var support = _factory.CreateClient().AsStaff(Guid.NewGuid().ToString(), StaffPermissions.CustomerView);
        using var anonymous = _factory.CreateClient();

        (await customer.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await support.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/v1/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

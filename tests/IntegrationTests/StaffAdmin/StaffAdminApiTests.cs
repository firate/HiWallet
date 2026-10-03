using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.StaffAdmin;

/// <summary>
/// Personel yönetimi: izinler kodda, roller, çalışanlar ve atamalar personel yönetiminin
/// veritabanında. Kimlik sağlayıcının yerinde sahte dizin, veritabanı gerçek. Her değişiklik
/// işi yapan çalışanla kayıt altında; çalışan kendine yetki veremiyor. Rolü alınan ya da
/// kapatılan çalışanın bir sonraki isteği reddediliyor.
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

        // Testin yöneticisi: kayıtlı bir çalışan ve kurulumun açtığı yönetici rolünde.
        _adminId = await _factory.AddAdminAsync(ct);
        _admin.AsStaff(_adminId.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    private static string NewRoleName() => $"Rol {Guid.NewGuid():N}"[..20];

    private HttpClient StaffClient(Guid staffId) => _factory.CreateClient().AsStaff(staffId.ToString());

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
    /// Açılışta yönetici rolü açılıyor ve kimsede personel yönetimi yoksa ilk yöneticiye
    /// davet gidiyor. Kimlik sağlayıcının konsoluna girilmiyor. Yöneticisi olmayan ayrı bir
    /// veritabanında: ortak veritabanında başka testlerin yöneticileri var.
    /// </summary>
    [Fact]
    public async Task Kurulum_YoneticiRolunuVeIlkYoneticiyiKurar()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ilk-{Guid.NewGuid():N}@ornek.com";
        await using var fresh = new StaffAdminFixture();
        await fresh.InitializeAsync();
        await using var factory = new StaffAdminApiFactory(fresh, email);
        using var _ = await factory.CreateReadyClientAsync(ct);

        await using var db = fresh.CreateContext();
        var role = await db.Roles.SingleAsync(ct);
        role.Name.ShouldBe(StaffAdminApiFactory.AdminRoleName);
        role.Permissions.ShouldBe([StaffPermissions.StaffManage]);

        var admin = await db.Members.SingleAsync(ct);
        admin.Email.ShouldBe(email);
        (await db.RoleAssignments.SingleAsync(ct)).ShouldSatisfyAllConditions(
            a => a.StaffId.ShouldBe(admin.Id),
            a => a.RoleId.ShouldBe(role.Id));
        factory.Directory.Invitations.ShouldBe([admin.Id]);

        (await db.AuditEvents.Where(e => e.TargetId == admin.Id).SingleAsync(ct)).ShouldSatisfyAllConditions(
            e => e.Action.ShouldBe(StaffAuditAction.StaffInvited),
            e => e.ActorSubject.ShouldBe(Actor.System.Subject));
    }

    // --- Anlık yetki ---

    /// <summary>Çalışanın kendisi: rolleri ve izinleri. İlk görüldüğü an davetini tamamlamış sayılıyor.</summary>
    [Fact]
    public async Task Me_SuAnkiRolleriVeIzinleri_IlkGirisDaveti_Tamamlar()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        await CreateRoleAsync(name, ct, StaffPermissions.WithdrawalReview, StaffPermissions.CustomerView);
        var staffId = await _factory.AddStaffAsync(ct, name);

        (await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff/{staffId}", ct))
            .GetProperty("invitationPending").GetBoolean().ShouldBeTrue();

        using var staff = StaffClient(staffId);
        var me = await staff.GetFromJsonAsync<JsonElement>("/v1/me", ct);

        me.GetProperty("subject").GetString().ShouldBe(staffId.ToString());
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe([name]);
        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .ShouldBe([StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview]);

        (await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff/{staffId}", ct))
            .GetProperty("invitationPending").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// Rolü alınan çalışanın token'ı hâlâ geçerli ama bir sonraki isteği reddediliyor:
    /// izin token'da değil, her istekte veritabanından okunuyor.
    /// </summary>
    [Fact]
    public async Task RolAlininca_AyniTokenlaBirSonrakiIstek403()
    {
        var ct = TestContext.Current.CancellationToken;
        var other = await _factory.AddAdminAsync(ct);
        using var otherAdmin = StaffClient(other);

        (await otherAdmin.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PutAsJsonAsync($"/v1/staff/{other}/roles", new { roleIds = Array.Empty<Guid>() }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await otherAdmin.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await otherAdmin.GetFromJsonAsync<JsonElement>("/v1/me", ct))
            .GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    /// <summary>Rolün izni alınınca roldeki herkes bir sonraki isteğinde onu kaybediyor.</summary>
    [Fact]
    public async Task RolunIzniAlininca_UyelerininBirSonrakiIstegi403()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        var roleId = await CreateRoleAsync(name, ct, StaffPermissions.StaffManage, StaffPermissions.CustomerView);
        var member = await _factory.AddStaffAsync(ct, name);
        using var staff = StaffClient(member);

        (await staff.GetAsync("/v1/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PutAsJsonAsync($"/v1/roles/{roleId}", new { permissions = new[] { StaffPermissions.CustomerView } }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await staff.GetAsync("/v1/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kapatilinca_AyniTokenlaBirSonrakiIstek403_IzinleriBos()
    {
        var ct = TestContext.Current.CancellationToken;
        var other = await _factory.AddAdminAsync(ct);
        using var otherAdmin = StaffClient(other);
        (await otherAdmin.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PostAsync($"/v1/staff/{other}/disable", null, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await otherAdmin.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var me = await otherAdmin.GetFromJsonAsync<JsonElement>("/v1/me", ct);
        me.GetProperty("roles").GetArrayLength().ShouldBe(0);
        me.GetProperty("permissions").GetArrayLength().ShouldBe(0);
    }

    /// <summary>Kimlik sağlayıcının konsolunda açılan kullanıcının token'ı geçerli ama izni yok.</summary>
    [Fact]
    public async Task PaneldenAcilmamisCalisan_IzniYok()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stranger = StaffClient(_factory.Directory.AddUser($"konsol-{Guid.NewGuid():N}@ornek.com"));

        (await stranger.GetFromJsonAsync<JsonElement>("/v1/me", ct)).GetProperty("permissions").GetArrayLength().ShouldBe(0);
        (await stranger.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

        var updated = await _admin.PutAsJsonAsync($"/v1/roles/{roleId}", new
        {
            description = "Operasyon ekibi",
            permissions = new[] { StaffPermissions.WithdrawalReview, StaffPermissions.CustomerView }
        }, ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()).ShouldBe([StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview]);

        (await _admin.DeleteAsync($"/v1/roles/{roleId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _admin.GetAsync($"/v1/roles/{roleId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var events = await EventsForAsync(roleId, ct);
        events.Select(e => e.Action).ShouldBe(
            [StaffAuditAction.RoleCreated, StaffAuditAction.RoleUpdated, StaffAuditAction.RoleDeleted]);
        events.ShouldAllBe(e => e.ActorSubject == _adminId.ToString() && e.TargetLabel == name);
        events[1].Details.ShouldContain(StaffPermissions.WithdrawalReview);
    }

    /// <summary>Silinen rolün atamaları da gidiyor; kimin yetkisini kaybettiği kayıtta.</summary>
    [Fact]
    public async Task Rol_Silinince_UyelerdenAlinir_KayittaUyeler()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        var roleId = await CreateRoleAsync(name, ct, StaffPermissions.CustomerView);
        var member = await _factory.AddStaffAsync(ct, name);

        var detail = await _admin.GetFromJsonAsync<JsonElement>($"/v1/roles/{roleId}", ct);
        detail.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("staffId").GetGuid()).ShouldBe([member]);

        (await _admin.DeleteAsync($"/v1/roles/{roleId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _factory.RoleNamesOfAsync(member, ct)).ShouldBeEmpty();
        (await EventsForAsync(roleId, ct))[^1].Details.ShouldContain("calisan-");
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
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("role_exists");
    }

    /// <summary>Sahip olduğu rolü değiştiren çalışan kendine yetki vermiş olurdu.</summary>
    [Fact]
    public async Task KendiRolunuDegistiremezSilemez_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        var roleId = await CreateRoleAsync(name, ct, StaffPermissions.CustomerView);
        await _factory.AssignAsync(_adminId, ct, name);

        var update = await _admin.PutAsJsonAsync($"/v1/roles/{roleId}", new
        {
            permissions = new[] { StaffPermissions.CustomerView, StaffPermissions.PromoGrant }
        }, ct);
        var delete = await _admin.DeleteAsync($"/v1/roles/{roleId}", ct);

        update.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await update.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("own_role");
        delete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await _admin.GetFromJsonAsync<JsonElement>($"/v1/roles/{roleId}", ct)).GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()).ShouldBe([StaffPermissions.CustomerView]);
    }

    // --- Çalışanlar ---

    [Fact]
    public async Task Davet_KullaniciyiAcarDavetGonderirKaydeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var roleId = await CreateRoleAsync(NewRoleName(), ct, StaffPermissions.CustomerView);
        var email = $"yeni-{Guid.NewGuid():N}@ornek.com";

        var response = await _admin.PostAsJsonAsync("/v1/staff", new
        {
            email = email.ToUpperInvariant(),
            firstName = "Ayşe",
            lastName = "Yılmaz",
            roleIds = new[] { roleId }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        var staff = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var staffId = staff.GetProperty("staffId").GetGuid();
        staff.GetProperty("email").GetString().ShouldBe(email);
        staff.GetProperty("invitationPending").GetBoolean().ShouldBeTrue();
        staff.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("roleId").GetGuid()).ShouldBe([roleId]);

        _factory.Directory.Invitations.ShouldContain(staffId);
        var events = await EventsForAsync(staffId, ct);
        events.ShouldHaveSingleItem().Action.ShouldBe(StaffAuditAction.StaffInvited);
        events[0].TargetLabel.ShouldBe(email);
    }

    [Fact]
    public async Task Davet_KayitliCalisan_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var staffId = await _factory.AddStaffAsync(ct);
        var email = (await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff/{staffId}", ct)).GetProperty("email").GetString();

        var response = await _admin.PostAsJsonAsync("/v1/staff", new { email, roleIds = Array.Empty<Guid>() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("staff_exists");
    }

    /// <summary>
    /// Kimlik sağlayıcıda kullanıcısı olup kaydı olmayan adres (önceki davet kaydı yazamadan
    /// kaldı): yeni kullanıcı açılmıyor, o kullanıcı kaydediliyor.
    /// </summary>
    [Fact]
    public async Task Davet_KimlikSaglayicidaKalanKullanici_OKullaniciylaTamamlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"yarim-{Guid.NewGuid():N}@ornek.com";
        var existing = _factory.Directory.AddUser(email);

        var response = await _admin.PostAsJsonAsync("/v1/staff", new { email, roleIds = Array.Empty<Guid>() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("staffId").GetGuid().ShouldBe(existing);
        _factory.Directory.Invitations.ShouldContain(existing);
    }

    [Fact]
    public async Task Davet_OlmayanRol_422()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _admin.PostAsJsonAsync("/v1/staff", new
        {
            email = $"x-{Guid.NewGuid():N}@ornek.com",
            roleIds = new[] { Guid.NewGuid() }
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("rule").GetString().ShouldBe("unknown_role");
    }

    [Fact]
    public async Task Rolleri_Degisir_OncesiVeSonrasiKayitta()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = NewRoleName();
        var second = NewRoleName();
        await CreateRoleAsync(first, ct, StaffPermissions.CustomerView);
        var secondId = await CreateRoleAsync(second, ct, StaffPermissions.WithdrawalReview);
        var staffId = await _factory.AddStaffAsync(ct, first);

        var response = await _admin.PutAsJsonAsync($"/v1/staff/{staffId}/roles", new { roleIds = new[] { secondId } }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.RoleNamesOfAsync(staffId, ct)).ShouldBe([second]);
        var change = (await EventsForAsync(staffId, ct)).ShouldHaveSingleItem();
        change.Action.ShouldBe(StaffAuditAction.StaffRolesChanged);
        var details = JsonDocument.Parse(change.Details).RootElement;
        details.GetProperty("before").EnumerateArray().Select(r => r.GetString()).ShouldBe([first]);
        details.GetProperty("after").EnumerateArray().Select(r => r.GetString()).ShouldBe([second]);
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
        (await _factory.RoleNamesOfAsync(_adminId, ct)).ShouldBe([StaffAdminApiFactory.AdminRoleName]);
    }

    [Fact]
    public async Task Kapatilir_OturumuKapanir_YenidenAcilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var staffId = await _factory.AddStaffAsync(ct);

        var disabled = await _admin.PostAsync($"/v1/staff/{staffId}/disable", null, ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await disabled.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
        _factory.Directory.IsEnabled(staffId).ShouldBeFalse();
        _factory.Directory.SignedOut.ShouldContain(staffId);

        var enabled = await _admin.PostAsync($"/v1/staff/{staffId}/enable", null, ct);
        (await enabled.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("enabled").GetBoolean().ShouldBeTrue();
        _factory.Directory.IsEnabled(staffId).ShouldBeTrue();

        (await EventsForAsync(staffId, ct)).Select(e => e.Action)
            .ShouldBe([StaffAuditAction.StaffDisabled, StaffAuditAction.StaffEnabled]);
    }

    [Fact]
    public async Task Davet_YenidenGonderilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var staffId = await _factory.AddStaffAsync(ct);

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
            (await _admin.PostAsJsonAsync("/v1/staff", new { email = $"ara{tag}-{i}@ornek.com", roleIds = Array.Empty<Guid>() }, ct))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var first = await _admin.GetFromJsonAsync<JsonElement>($"/v1/staff?search=ARA{tag}&size=2", ct);
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
    }

    /// <summary>
    /// Yalnızca çalışanların kimlik sağlayıcısının token'ı ve personel yönetimi izniyle.
    /// Müşterinin token'ını bu servis tanımıyor.
    /// </summary>
    [Fact]
    public async Task Yetki_MusteriTanimiyor_IzinsizCalisan403()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = NewRoleName();
        await CreateRoleAsync(name, ct, StaffPermissions.CustomerView);
        using var customer = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");
        using var support = StaffClient(await _factory.AddStaffAsync(ct, name));
        using var anonymous = _factory.CreateClient();

        (await customer.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await customer.GetAsync("/v1/me", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await support.GetAsync("/v1/roles", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/v1/staff", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

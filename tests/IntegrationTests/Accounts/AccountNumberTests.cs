using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HiWallet.IntegrationTests.Accounts;

/// <summary>
/// Hesabın insanın kullandığı numarası: açılışta veriliyor, değişmiyor, panelde ve
/// uygulamada kimlik yerine o yazılıyor. Kimlik (GUID) içeride kalıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountNumberTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WalletApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WalletApiFactory(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string NumberOf(JsonElement body)
    {
        var value = body.GetProperty("accountNumber").GetString();
        AccountNumber.TryFrom(value, out _).ShouldBeTrue(value);
        return value!;
    }

    private async Task<(Guid AccountId, string Number)> SeededAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        await LedgerSeeder.CreateWalletAsync(db, accountId, "Ana", ct);
        var number = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Number).SingleAsync(ct);

        return (accountId, number.Value);
    }

    /// <summary>Kayıt tekrar edilebilir; tekrar eden açılış yeni numara üretmiyor.</summary>
    [Fact]
    public async Task BireyselHesap_NumarayaAcilir_TekrardaAyniNumara()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = $"kayit-{Guid.NewGuid():N}";
        using var onboarding = _factory.CreateClient().AsOnboarding();

        var first = await onboarding.PostAsJsonAsync("/v1/person-accounts", new { holder }, ct);
        var again = await onboarding.PostAsJsonAsync("/v1/person-accounts", new { holder }, ct);

        var number = NumberOf(await first.Content.ReadFromJsonAsync<JsonElement>(ct));
        NumberOf(await again.Content.ReadFromJsonAsync<JsonElement>(ct)).ShouldBe(number);
    }

    [Fact]
    public async Task IsyeriHesabi_NumarayaAcilir_DetaydaVeListedeAyni()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient().As($"isyeri-{Guid.NewGuid():N}");

        var opened = await client.PostAsJsonAsync("/v1/accounts", new { type = nameof(AccountType.Business) }, ct);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join("\n", _factory.Errors));
        var body = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        var number = NumberOf(body);
        var accountId = body.GetProperty("accountId").GetGuid();

        NumberOf(await client.GetFromJsonAsync<JsonElement>($"/v1/accounts/{accountId}", ct)).ShouldBe(number);
        NumberOf((await client.GetFromJsonAsync<JsonElement>("/v1/accounts", ct)).GetProperty("items")[0])
            .ShouldBe(number);
    }

    /// <summary>
    /// Numarayla kayıt: sahibi ve görüntüleme izni olan çalışan görüyor. Başkasının
    /// hesabı 404: numaranın var olduğu da dışarı verilmiyor.
    /// </summary>
    [Fact]
    public async Task NumarayaGore_SahibiVeCalisanGorur_BaskasiGoremez()
    {
        var ct = TestContext.Current.CancellationToken;
        var (accountId, number) = await SeededAsync(ct);
        using var owner = _factory.CreateClient().AsOwnerOf(accountId);
        using var stranger = _factory.CreateClient().As($"yabanci-{Guid.NewGuid():N}");
        using var support = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);
        using var unauthorized = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}");

        (await owner.GetFromJsonAsync<JsonElement>($"/v1/accounts/by-number/{number}", ct))
            .GetProperty("accountId").GetGuid().ShouldBe(accountId);
        (await support.GetFromJsonAsync<JsonElement>($"/v1/accounts/by-number/{number}", ct))
            .GetProperty("accountId").GetGuid().ShouldBe(accountId);
        (await stranger.GetAsync($"/v1/accounts/by-number/{number}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unauthorized.GetAsync($"/v1/accounts/by-number/{number}", ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Kontrol hanesi tutmayan numara yazım hatası: 400. Geçerli ama olmayan numara 404.</summary>
    [Fact]
    public async Task NumarayaGore_KontrolHanesiTutmuyor400_Olmayan404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var support = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);

        (await support.GetAsync("/v1/accounts/by-number/1234567890", ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var unused = AccountNumber.New();
        await using (var db = postgres.CreateContext())
        {
            (await db.Accounts.AnyAsync(a => a.Number == unused, ct)).ShouldBeFalse();
        }

        (await support.GetAsync($"/v1/accounts/by-number/{unused}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Numaradan önce açılmış hesaplar migration'da numara alıyor; her para biriminde en
    /// eski cüzdan varsayılan oluyor. Ayrı bir schema'da: önceki şemaya kadar migrate
    /// edilip eski biçimde satır yazılıyor.
    /// </summary>
    [Fact]
    public async Task Migration_EskiHesaplaraNumaraVeVarsayilanCuzdanVerir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var old = new PostgresFixture();
        await old.CreateSchemaAsync();
        await using var db = old.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20260928092837_AddKycLevel", cancellationToken: ct);

        var (person, business) = (Guid.NewGuid(), Guid.NewGuid());
        var (older, newer, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO accounts (id, type, holder, kyc_level, accepts_promo, created_at) VALUES
               ({person}, 'person', 'eski-kimlik', 'unknown', false, '2026-01-01'),
               ({business}, 'business', NULL, NULL, false, '2026-01-01')
             """, ct);
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO ledger_accounts (id, type, account_id, name, currency, created_at) VALUES
               ({newer}, 'user_wallet', {person}, 'Yeni', 'TRY', '2026-02-01'),
               ({older}, 'user_wallet', {person}, 'Eski', 'TRY', '2026-01-01'),
               ({other}, 'user_wallet', {business}, 'Kasa', 'TRY', '2026-01-01')
             """, ct);

        await db.Database.MigrateAsync(ct);

        var numbers = await db.Accounts.Where(a => a.Id == person || a.Id == business).Select(a => a.Number).ToListAsync(ct);
        numbers.Count.ShouldBe(2);
        numbers.Distinct().Count().ShouldBe(2);

        var defaults = await db.DefaultWallets.ToListAsync(ct);
        defaults.ShouldContain(d => d.AccountId == person && d.WalletId == older);
        defaults.ShouldContain(d => d.AccountId == business && d.WalletId == other);
        defaults.Count.ShouldBe(2);
    }
}

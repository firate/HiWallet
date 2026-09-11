using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Ledger;

/// <summary>
/// İşlemin aktörü ledger'a yazılıyor mu (<c>decisions.md</c> madde 34).
///
/// İki ayrı şey sınanıyor ve ikisi de gerekli: değer gidip geri geliyor mu, ve
/// <b>veritabanı bunu dayatıyor mu</b>. İkincisi olmadan kural yalnızca bir C#
/// sözü olurdu — ve bu projede kod seviyesindeki niyetin yetmediği bir kez
/// görüldü (<c>REVOKE CONNECT</c>, <c>verify-compose.md</c>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActorTests(PostgresFixture postgres)
{
    /// <summary>
    /// Sistem aktörü yazılıp okunuyor. <c>ValueConverters</c> üzerinden gidip
    /// geldiği için metin eşlemesi de bu testte doğrulanmış oluyor.
    /// </summary>
    [Fact]
    public async Task SistemAktoru_YazilipOkunuyor()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Aktör testi", ct);

        // FundAsync bir top-up işlemi yazıyor.
        await LedgerSeeder.FundAsync(db, wallet, 100m, ct);

        var tx = await db.LedgerTransactions.AsNoTracking()
            .Where(t => t.LedgerAccountId == wallet && t.Type == LedgerTransactionType.Topup)
            .SingleAsync(ct);

        tx.ActorType.ShouldBe(ActorType.System);
        tx.ActorId.ShouldBe("topup");
    }

    /// <summary>
    /// <b>Asıl kanıt.</b> Aktörsüz bir satır veritabanına GİREMİYOR.
    ///
    /// Migration'ın kolon varsayılanını düşürmesi bu yüzden şart: varsayılan
    /// kalsaydı bu INSERT sessizce başarılı olur ve aktör bilgisi uydurulmuş bir
    /// değerle dolardı. Test o düzeltmenin bekçisi.
    /// </summary>
    [Fact]
    public async Task AktorsuzSatir_VeritabaninaGiremez()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Aktörsüz", ct);

        var exception = await Should.ThrowAsync<PostgresException>(
            db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ledger_transactions (id, type, ledger_account_id, created_at)
                VALUES (gen_random_uuid(), 'p2p', {0}, now())
                """,
                [wallet],
                ct));

        // 23502 = not_null_violation. Kolonun varsayılanı olsaydı hata HİÇ çıkmazdı.
        exception.SqlState.ShouldBe("23502");
    }

    /// <summary>
    /// Müşteri aktörü hesabın kimliğini taşıyor — cüzdanın değil. Cüzdan yazılsaydı
    /// aynı hesabın ikinci cüzdanından yapılan işlem başka biri yapmış gibi
    /// görünürdü (madde 20: bir hesabın aynı para biriminde birden fazla cüzdanı
    /// olabiliyor).
    /// </summary>
    [Fact]
    public void MusteriAktoru_HesapKimliginiTasir()
    {
        var accountId = Guid.NewGuid();

        var actor = Actor.Customer(accountId);

        actor.Type.ShouldBe(ActorType.Customer);
        actor.Id.ShouldBe(accountId.ToString());
    }
}

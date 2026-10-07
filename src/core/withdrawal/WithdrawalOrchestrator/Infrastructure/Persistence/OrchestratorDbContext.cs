using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Orchestrator'ın kendi veritabanı (<c>hiwallet_withdrawal</c>). Wallet tablolarına
/// DOKUNMUYOR — oraya yalnızca komut gönderiliyor (CLAUDE.md "Servis sınırı").
///
/// <b>Neden tek rol?</b> Wallet'taki iki rollü kurulumun sebebi <c>ledger_entries</c>
/// üzerindeki <c>REVOKE</c>'un yalnızca tablo sahibi OLMAYAN bir role işlemesi. Burada
/// append-only bir tablo yok: saga satırı her geçişte güncelleniyor, outbox satırı
/// yayınlandıkça. Zorlanacak bir garanti olmayınca ikinci rol yalnızca tören olurdu.
///
/// <b>İki tablo, tek transaction.</b> Varlık sebebi bu: saga geçişi ile göndereceği
/// komut aynı <c>SaveChanges</c>'te kalıcı oluyor (decisions.md madde 32).
/// </summary>
public sealed class OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
    : DbContext(options)
{
    public DbSet<WithdrawalSaga> Sagas => Set<WithdrawalSaga>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Eşlemeler tek tek uygulanıyor, assembly taraması yapılmıyor: hangi tablonun
        // bu şemada olduğu tek bakışta görünsün (structure.md "Adlandırma").
        modelBuilder.ApplyConfiguration(new Configurations.WithdrawalSagaConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.OutboxMessageConfiguration());
    }
}

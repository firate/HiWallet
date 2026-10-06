using Microsoft.EntityFrameworkCore;

namespace HiWallet.CardTopup.Infrastructure.Persistence;

/// <summary>
/// Kart yüklemesi servisinin kendi veritabanı (<c>hiwallet_card_topup</c>). Wallet
/// tablolarına DOKUNMUYOR: limit payını wallet-api'den istiyor, parayı wallet-consumer
/// yazıyor.
///
/// <b>Tek rol</b>, orchestrator'daki gerekçeyle: append-only zorlanacak tablo yok, yükleme
/// satırı her geçişte güncelleniyor, outbox satırı yayınlandıkça.
/// </summary>
public sealed class CardTopupDbContext(DbContextOptions<CardTopupDbContext> options) : DbContext(options)
{
    public DbSet<Domain.CardTopup> CardTopups => Set<Domain.CardTopup>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Configurations.CardTopupConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.OutboxMessageConfiguration());
    }
}

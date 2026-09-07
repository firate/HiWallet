using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.TopupWebhook.Infrastructure.Persistence;

/// <summary>
/// Bu servisin kendi veritabanı (<c>hiwallet_topup</c>). Wallet şemasına
/// DOKUNMUYOR — servis sınırı (CLAUDE.md "Servis sınırı").
///
/// <b>Neden tek rol?</b> Wallet tarafındaki iki rollü kurulumun sebebi
/// <c>ledger_entries</c> üzerindeki <c>REVOKE</c>'un yalnızca tablo sahibi OLMAYAN
/// bir role işlemesi. Burada append-only bir tablo yok: <c>topup_inbox</c> satırları
/// yayınlandıkça güncelleniyor. Zorlanacak bir garanti olmayınca ikinci rol yalnızca
/// tören olurdu, o yüzden yok.
/// </summary>
public sealed class InboxDbContext(DbContextOptions<InboxDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        // Tablo adı topup_inbox KALIYOR, artık settlement de taşısa bile: yeniden
        // adlandırmak çalışan bir tabloyu bir isim uğruna migration'a sokmak olurdu.
        // "Sağlayıcıdan gelen imzalı bildirimlerin defteri" hep buydu.
        builder.ToTable("topup_inbox");

        builder.HasKey(m => m.Id).HasName("pk_topup_inbox");

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(m => m.EventId).HasColumnName("event_id").HasColumnType("text");

        builder.Property(m => m.Kind)
            .HasColumnName("kind")
            .HasColumnType("text")
            .HasConversion(k => k.ToText(), text => InboxKinds.FromText(text));

        builder.Property(m => m.RoutingKey).HasColumnName("routing_key").HasColumnType("text");

        // jsonb: sorgulanabilir olsun diye. Teşhis sırasında "şu cüzdana gelen
        // yükleme neydi" sorusu payload üzerinden cevaplanıyor.
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(m => m.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");

        builder.Property(m => m.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("now()");
        builder.Property(m => m.PublishedAt).HasColumnName("published_at");
        builder.Property(m => m.PublishAttempts).HasColumnName("publish_attempts").HasDefaultValue(0);
        builder.Property(m => m.LastError).HasColumnName("last_error").HasColumnType("text");

        // Idempotency'nin birinci kademesi. Aynı event iki kez POST edilirse ikincisi
        // buraya takılıyor ve kuyruğa hiç girmiyor (overview.md madde 5).
        builder.HasIndex(m => new { m.Provider, m.EventId })
            .HasDatabaseName("ux_topup_inbox_event")
            .IsUnique();

        // Relay'in sıcak sorgusu: yayınlanmamışlar, geliş sırasına göre. Partial
        // index — yayınlanmış satırlar (zamanla tablonun tamamı) index'e girmiyor.
        //
        // kind index'e GİRMİYOR: relay iki akışı tek turda, tek batch'te alıyor.
        // Ayrı ayrı sorgulasaydı iki tur, iki kilit ve iki commit olurdu; kazanç yok.
        builder.HasIndex(m => m.ReceivedAt)
            .HasDatabaseName("ix_topup_inbox_unpublished")
            .HasFilter("published_at IS NULL");
    }
}

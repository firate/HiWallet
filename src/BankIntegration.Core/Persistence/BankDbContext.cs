using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.BankIntegration.Persistence;

/// <summary>
/// Banka entegrasyonunun veritabanı (<c>hiwallet_bank</c>). İki host paylaşıyor:
/// <c>bank-adapter</c> ve <c>bank-webhook</c>.
///
/// Wallet ve orchestrator şemalarına DOKUNMUYOR; sahte bankanın veritabanını da
/// görmüyor (decisions.md madde 7, 35).
/// </summary>
/// <remarks>
/// Sınıf public, <c>DbSet</c>'ler public: iki ayrı host'tan erişiliyor, dolayısıyla
/// <c>internal</c> olamazlar.
/// </remarks>
public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<BankTransfer> Transfers => Set<BankTransfer>();

    public DbSet<BankCallback> Callbacks => Set<BankCallback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BankTransferConfiguration());
        modelBuilder.ApplyConfiguration(new BankCallbackConfiguration());
    }
}

internal sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> builder)
    {
        builder.ToTable("bank_transfers");

        // PK doğrudan CommandId: ayrı bir yüzey id yok. Komut deduplikasyonu ile
        // "ne yaptık" kaydı aynı satır, dolayısıyla ikisi ayrışamıyor.
        builder.HasKey(t => t.CommandId).HasName("pk_bank_transfers");

        builder.Property(t => t.CommandId).HasColumnName("command_id");
        builder.Property(t => t.SagaId).HasColumnName("saga_id");

        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(t => t.Fee).HasColumnName("fee").HasColumnType("numeric(19,4)");
        builder.Property(t => t.Currency).HasColumnName("currency").HasColumnType("char(3)");

        builder.Property(t => t.DestinationIban).HasColumnName("destination_iban").HasColumnType("text");
        builder.Property(t => t.Status).HasColumnName("status").HasColumnType("text");
        builder.Property(t => t.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(t => t.FailureReason).HasColumnName("failure_reason").HasColumnType("text");

        builder.Property(t => t.StartedAt).HasColumnName("started_at");
        builder.Property(t => t.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(t => t.ResolvedVia).HasColumnName("resolved_via").HasColumnType("text");

        builder.Property(t => t.ReplyRoutingKey).HasColumnName("reply_routing_key").HasColumnType("text");
        builder.Property(t => t.ReplyPayload).HasColumnName("reply_payload").HasColumnType("text");
        builder.Property(t => t.ReplyPublishedAt).HasColumnName("reply_published_at");
        builder.Property(t => t.PublishAttempts).HasColumnName("publish_attempts").HasDefaultValue(0);
        builder.Property(t => t.LastError).HasColumnName("last_error").HasColumnType("text");

        // Bankanın referansıyla satır bulma: callback ve durum sorgusu bu yoldan
        // geliyor. UNIQUE DEĞİL — banka kabul edene kadar NULL ve NULL'lar unique
        // index'te eşleşmediği için tekillik zaten kurulmuyordu; kısmi index olarak
        // kurmak da yanlış bir güvence verirdi.
        builder.HasIndex(t => t.BankReference)
            .HasDatabaseName("ix_bank_transfers_reference")
            .HasFilter("bank_reference IS NOT NULL");

        // Mutabakat taramasının sıcak sorgusu: "hâlâ bekleyen transferler".
        // Kısmi index, kapanmış transferler taranmasın diye — tablo büyüdükçe fark
        // açılıyor ve bu tabloda satırlar hiç silinmiyor.
        builder.HasIndex(t => t.StartedAt)
            .HasDatabaseName("ix_bank_transfers_pending")
            .HasFilter("resolved_at IS NULL");

        // Cevap relay'inin sıcak sorgusu: "sonucu öğrenilmiş ama yayınlanmamış".
        builder.HasIndex(t => t.ResolvedAt)
            .HasDatabaseName("ix_bank_transfers_unpublished")
            .HasFilter("resolved_at IS NOT NULL AND reply_published_at IS NULL");
    }
}

internal sealed class BankCallbackConfiguration : IEntityTypeConfiguration<BankCallback>
{
    public void Configure(EntityTypeBuilder<BankCallback> builder)
    {
        builder.ToTable("bank_callbacks");

        builder.HasKey(c => c.Id).HasName("pk_bank_callbacks");

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(c => c.EventId).HasColumnName("event_id").HasColumnType("text");
        builder.Property(c => c.RawPayload).HasColumnName("raw_payload").HasColumnType("text");

        builder.Property(c => c.ReceivedAt).HasColumnName("received_at");
        builder.Property(c => c.ProcessedAt).HasColumnName("processed_at");
        builder.Property(c => c.ProcessAttempts).HasColumnName("process_attempts").HasDefaultValue(0);
        builder.Property(c => c.LastError).HasColumnName("last_error").HasColumnType("text");

        // Bankanın tekrar gönderdiği bildirim ikinci kez işlenmiyor. Kurum başına
        // tekillik: iki bankanın çakışan event id'leri birbirini bastırmasın.
        builder.HasIndex(c => new { c.Provider, c.EventId })
            .HasDatabaseName("ux_bank_callbacks_event")
            .IsUnique();

        // Relay'in sıcak sorgusu: işlenmemiş satırlar.
        builder.HasIndex(c => c.ReceivedAt)
            .HasDatabaseName("ix_bank_callbacks_unprocessed")
            .HasFilter("processed_at IS NULL");
    }
}

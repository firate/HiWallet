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

    /// <summary>Banka hesabımıza gelen havaleler ve wallet'a gidecek mesajları.</summary>
    public DbSet<BankDeposit> Deposits => Set<BankDeposit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BankTransferConfiguration());
        modelBuilder.ApplyConfiguration(new BankCallbackConfiguration());
        modelBuilder.ApplyConfiguration(new BankDepositConfiguration());
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
        builder.Property(t => t.ReturnsDepositId).HasColumnName("returns_deposit_id");

        builder.HasOne<BankDeposit>()
            .WithMany()
            .HasForeignKey(t => t.ReturnsDepositId)
            .HasConstraintName("fk_bank_transfers_returns_deposit")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.ReturnsDepositId)
            .HasDatabaseName("ix_bank_transfers_returns_deposit")
            .HasFilter("returns_deposit_id IS NOT NULL");
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

internal sealed class BankDepositConfiguration : IEntityTypeConfiguration<BankDeposit>
{
    public void Configure(EntityTypeBuilder<BankDeposit> builder)
    {
        builder.ToTable("bank_deposits", t =>
            t.HasCheckConstraint("ck_bank_deposits_via", "discovered_via IN ('callback','reconciliation')"));

        builder.HasKey(d => d.Id).HasName("pk_bank_deposits");

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(d => d.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(d => d.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(d => d.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(d => d.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(d => d.SenderName).HasColumnName("sender_name").HasColumnType("text");
        builder.Property(d => d.SenderIban).HasColumnName("sender_iban").HasColumnType("text");
        builder.Property(d => d.SenderNationalId).HasColumnName("sender_national_id").HasColumnType("text");
        builder.Property(d => d.ReceivedAt).HasColumnName("received_at");
        builder.Property(d => d.DiscoveredAt).HasColumnName("discovered_at");
        builder.Property(d => d.DiscoveredVia).HasColumnName("discovered_via").HasColumnType("text");
        builder.Property(d => d.Payload).HasColumnName("payload").HasColumnType("text");
        builder.Property(d => d.PublishedAt).HasColumnName("published_at");
        builder.Property(d => d.PublishAttempts).HasColumnName("publish_attempts").HasDefaultValue(0);
        builder.Property(d => d.LastError).HasColumnName("last_error").HasColumnType("text");

        // Aynı havale bildirimle de taramayla da gelebilir; ikincisi yazılmıyor. Banka
        // başına tekillik: iki bankanın referansları çakışabilir.
        builder.HasIndex(d => new { d.Provider, d.BankReference })
            .HasDatabaseName("ux_bank_deposits_reference")
            .IsUnique();

        // Relay'in sıcak sorgusu: yayınlanmamış havaleler.
        builder.HasIndex(d => d.DiscoveredAt)
            .HasDatabaseName("ix_bank_deposits_unpublished")
            .HasFilter("published_at IS NULL");
    }
}

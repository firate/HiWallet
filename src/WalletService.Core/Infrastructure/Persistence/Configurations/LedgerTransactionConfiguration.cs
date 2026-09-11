using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Şema: docs/ledger-schema.md "ledger_transactions".</summary>
internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions");

        builder.HasKey(t => t.Id).HasName("pk_ledger_transactions");

        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion(ValueConverters.LedgerTransactionType)
            .HasColumnType("text")
            .IsRequired();

        // "İsteği başlatan hesap" değil, işlemin idempotency KAPSAMI olan hesap.
        // İç işlemlerde de dolu — nullable olsaydı unique index'teki NULL'lar eşleşmez
        // ve aynı fatura iki kez yazılabilirdi (decisions.md madde 15).
        builder.Property(t => t.LedgerAccountId).HasColumnName("ledger_account_id").IsRequired();

        // İşlemi kim başlattı (decisions.md madde 34). İkisi de NOT NULL — nullable
        // bir kolon "müşteri yaptı" ile "kaydedilmedi"yi aynı değere indirirdi.
        builder.Property(t => t.ActorType)
            .HasColumnName("actor_type")
            .HasConversion(ValueConverters.ActorType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(t => t.ActorId)
            .HasColumnName("actor_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key").HasColumnType("text").IsRequired();
        builder.Property(t => t.CorrelationId).HasColumnName("correlation_id");

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(t => t.LedgerAccountId)
            .HasConstraintName("fk_ledger_transactions_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Partial DEĞİL: anahtar artık her satırda var (madde 4). Filtre kalsaydı
        // NULL yazabilen bir yol açıldığında index onu sessizce kapsam dışı bırakır
        // ve dedup o satırlar için çalışmazdı.
        builder.HasIndex(t => new { t.LedgerAccountId, t.IdempotencyKey })
            .HasDatabaseName("ux_ledger_tx_idem")
            .IsUnique();

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .HasConstraintName("fk_ledger_entries_transaction")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(t => t.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_entries");
    }
}

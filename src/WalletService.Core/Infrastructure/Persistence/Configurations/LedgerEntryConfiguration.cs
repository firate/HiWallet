using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Append-only. UPDATE ve DELETE migration içinde REVOKE ile de engellenir — EF yetki
/// modellemediği için o kısım <c>migrationBuilder.Sql(...)</c>.
/// Şema: docs/ledger-schema.md "ledger_entries".
/// </summary>
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries", t =>
            t.HasCheckConstraint("ck_ledger_entries_amount", "amount <> 0"));

        builder.HasKey(e => e.Id).HasName("pk_ledger_entries");

        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(e => e.TransactionId).HasColumnName("transaction_id").IsRequired();
        builder.Property(e => e.LedgerAccountId).HasColumnName("ledger_account_id").IsRequired();

        // İşaret yönü taşır: credit +, debit -. Ayrı direction kolonu YOK.
        builder.Property(e => e.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        builder.Property(e => e.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(e => e.Money);

        // Composite FK, tek kolonluğun yerine geçer: hesabın var olduğunu da garantiler,
        // entry'nin currency'sinin hesabınkiyle aynı olduğunu da. currency FK'ya KASTEN
        // gereksiz kolon olarak konuyor (decisions.md madde 17).
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(e => new { e.LedgerAccountId, e.Currency })
            .HasPrincipalKey(a => new { a.Id, a.Currency })
            .HasConstraintName("fk_ledger_entries_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.LedgerAccountId, e.Id })
            .HasDatabaseName("ix_ledger_entries_ledger_account");

        // Composite FK'nın index'i. EF bunu zaten otomatik üretiyor; burada yalnızca
        // adı veriliyor ki şemada PascalCase bir isim kalmasın.
        builder.HasIndex(e => new { e.LedgerAccountId, e.Currency })
            .HasDatabaseName("ix_ledger_entries_ledger_account_currency");

        // Zero-sum trigger'ı her entry insert'ünde bu index üzerinden okuyor.
        builder.HasIndex(e => e.TransactionId)
            .HasDatabaseName("ix_ledger_entries_tx");
    }
}

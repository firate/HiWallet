using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Askıdaki havalenin çözümünün adımları. Şema: docs/ledger-schema.md
/// "suspended_deposit_resolutions".
/// </summary>
internal sealed class SuspendedDepositResolutionConfiguration : IEntityTypeConfiguration<SuspendedDepositResolution>
{
    // Enum adı değil metin: yeniden adlandırma sessizce CHECK'i ihlal etmesin.
    private static readonly ValueConverter<DepositResolutionKind, string> Kind = new(
        kind => kind.ToText(), text => DepositResolutionKinds.FromText(text));

    public void Configure(EntityTypeBuilder<SuspendedDepositResolution> builder)
    {
        builder.ToTable("suspended_deposit_resolutions", t =>
        {
            t.HasCheckConstraint("ck_suspended_deposit_resolutions_kind",
                "kind IN ('moved','return_started','returned','return_failed')");
            // Hesap yalnızca aktarımda var: iade hesaba değil göndericiye gidiyor.
            t.HasCheckConstraint("ck_suspended_deposit_resolutions_account",
                "(kind = 'moved') = (account_id IS NOT NULL)");
            t.HasCheckConstraint("ck_suspended_deposit_resolutions_seq", "seq > 0");
        });

        // Anahtar havale ve sıra: aynı sırayı alan ikinci karar anahtara takılıyor.
        builder.HasKey(r => new { r.SuspendedDepositId, r.Seq }).HasName("pk_suspended_deposit_resolutions");

        builder.Property(r => r.SuspendedDepositId).HasColumnName("suspended_deposit_id");
        builder.Property(r => r.Seq).HasColumnName("seq");
        builder.Property(r => r.Kind).HasColumnName("kind").HasColumnType("text").HasConversion(Kind);
        builder.Property(r => r.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(r => r.AccountId).HasColumnName("account_id");
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by").HasColumnType("text").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasOne<SuspendedDeposit>()
            .WithMany()
            .HasForeignKey(r => r.SuspendedDepositId)
            .HasConstraintName("fk_suspended_deposit_resolutions_deposit")
            .OnDelete(DeleteBehavior.Restrict);

        // ledger_transactions'a FK YOK: satır kapı olarak ledger işleminden ÖNCE yazılıyor
        // (INSERT ... ON CONFLICT DO NOTHING) ve anlık bir FK onu reddederdi. İkisi aynı
        // transaction'da; ilişki join ile kuruluyor (processed_events ile aynı).

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .HasConstraintName("fk_suspended_deposit_resolutions_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.AccountId).HasDatabaseName("ix_suspended_deposit_resolutions_account");
    }
}

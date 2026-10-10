using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Askıdaki havaleler için verilen kararlar. Şema: docs/ledger-schema.md
/// "suspended_deposit_resolutions".
/// </summary>
internal sealed class SuspendedDepositResolutionConfiguration : IEntityTypeConfiguration<SuspendedDepositResolution>
{
    // Enum adı değil metin: yeniden adlandırma sessizce CHECK'i ihlal etmesin.
    private static readonly ValueConverter<DepositResolutionKind, string> Kind = new(
        kind => ToText(kind), text => FromText(text));

    public void Configure(EntityTypeBuilder<SuspendedDepositResolution> builder)
    {
        builder.ToTable("suspended_deposit_resolutions", t =>
            t.HasCheckConstraint("ck_suspended_deposit_resolutions_kind", "kind IN ('moved')"));

        // Anahtar havalenin kendisi: ikinci karar anahtara takılıyor.
        builder.HasKey(r => r.SuspendedDepositId).HasName("pk_suspended_deposit_resolutions");

        builder.Property(r => r.SuspendedDepositId).HasColumnName("suspended_deposit_id");
        builder.Property(r => r.Kind).HasColumnName("kind").HasColumnType("text").HasConversion(Kind);
        builder.Property(r => r.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(r => r.AccountId).HasColumnName("account_id");
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by").HasColumnType("text").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasOne<SuspendedDeposit>()
            .WithOne()
            .HasForeignKey<SuspendedDepositResolution>(r => r.SuspendedDepositId)
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

    private static string ToText(DepositResolutionKind kind) => kind switch
    {
        DepositResolutionKind.Moved => "moved",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static DepositResolutionKind FromText(string text) => text switch
    {
        "moved" => DepositResolutionKind.Moved,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, null)
    };
}

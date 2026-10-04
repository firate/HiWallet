using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Deposits;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Cüzdana geçirilemeyen havaleler. Şema: docs/ledger-schema.md "suspended_deposits".</summary>
internal sealed class SuspendedDepositConfiguration : IEntityTypeConfiguration<SuspendedDeposit>
{
    private static readonly ValueConverter<DepositHoldReason, string> Reason =
        new(reason => reason.ToText(), text => DepositHoldReasons.FromText(text));

    public void Configure(EntityTypeBuilder<SuspendedDeposit> builder)
    {
        builder.ToTable("suspended_deposits", t =>
        {
            // Enum adı değil metin: yeniden adlandırma sessizce CHECK'i ihlal etmesin.
            t.HasCheckConstraint("ck_suspended_deposits_reason",
                "reason IN ('no_account_number','ambiguous_account_number','unknown_account'," +
                "'business_account','no_wallet_in_currency','unknown_sender','sender_not_holder'," +
                "'limit_exceeded')");
            t.HasCheckConstraint("ck_suspended_deposits_amount", "amount > 0");
        });

        builder.HasKey(d => d.LedgerTransactionId).HasName("pk_suspended_deposits");

        builder.Property(d => d.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(d => d.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(d => d.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(d => d.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");

        builder.Property(d => d.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(d => d.Reason)
            .HasColumnName("reason")
            .HasColumnType("text")
            .HasConversion(Reason);

        builder.Property(d => d.AccountId).HasColumnName("account_id");
        builder.Property(d => d.ReceivedAt).HasColumnName("received_at");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");

        builder.Ignore(d => d.Money);

        builder.HasOne<LedgerTransaction>()
            .WithOne()
            .HasForeignKey<SuspendedDeposit>(d => d.LedgerTransactionId)
            .HasConstraintName("fk_suspended_deposits_transaction")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(d => d.AccountId)
            .HasConstraintName("fk_suspended_deposits_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Aynı banka hareketi ikinci kez askıya alınmıyor. Asıl kapı tüketicideki
        // processed_events; bu ikinci emniyet kemeri, ledger'daki unique index gibi.
        builder.HasIndex(d => new { d.Provider, d.BankReference })
            .HasDatabaseName("ux_suspended_deposits_reference")
            .IsUnique();

        // Panelin listesi: en yeniler önce.
        builder.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_suspended_deposits_created");

        builder.HasIndex(d => d.AccountId)
            .HasDatabaseName("ix_suspended_deposits_account")
            .HasFilter("account_id IS NOT NULL");
    }
}

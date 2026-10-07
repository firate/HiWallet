using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.CardTopups;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Kartla yüklemenin limitten ayırdığı pay. Şema: docs/ledger-schema.md "card_topup_holds".</summary>
internal sealed class CardTopupHoldConfiguration : IEntityTypeConfiguration<CardTopupHold>
{
    public void Configure(EntityTypeBuilder<CardTopupHold> builder)
    {
        builder.ToTable("card_topup_holds", t =>
            t.HasCheckConstraint("ck_card_topup_holds_amount", "amount > 0"));

        builder.HasKey(h => h.Id).HasName("pk_card_topup_holds");

        // Kimliği kart yüklemesi servisi veriyor; aynı kimlikle ikinci istek aynı payı buluyor.
        builder.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(h => h.AccountId).HasColumnName("account_id");
        builder.Property(h => h.WalletId).HasColumnName("wallet_id");
        builder.Property(h => h.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");

        builder.Property(h => h.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(h => h.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(h => h.CreatedAt).HasColumnName("created_at");

        builder.Ignore(h => h.Money);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(h => h.AccountId)
            .HasConstraintName("fk_card_topup_holds_account")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(h => h.WalletId)
            .HasConstraintName("fk_card_topup_holds_wallet")
            .OnDelete(DeleteBehavior.Restrict);

        // Seviye limitinin sıcak sorgusu: hesabın açık payları.
        builder.HasIndex(h => h.AccountId).HasDatabaseName("ix_card_topup_holds_account");

        builder.HasIndex(h => h.WalletId).HasDatabaseName("ix_card_topup_holds_wallet");
    }
}

/// <summary>Kartla yüklemenin kapanışı. Şema: docs/ledger-schema.md "card_topup_hold_closures".</summary>
internal sealed class CardTopupHoldClosureConfiguration : IEntityTypeConfiguration<CardTopupHoldClosure>
{
    private static readonly ValueConverter<CardTopupOutcome, string> Outcome =
        new(outcome => outcome.ToText(), text => CardTopupOutcomes.FromText(text));

    public void Configure(EntityTypeBuilder<CardTopupHoldClosure> builder)
    {
        builder.ToTable("card_topup_hold_closures", t =>
        {
            t.HasCheckConstraint("ck_card_topup_hold_closures_outcome", "outcome IN ('paid','failed')");

            // Ödenen yüklemenin ledger işlemi var, ödenmeyeninki yok.
            t.HasCheckConstraint(
                "ck_card_topup_hold_closures_transaction",
                "(outcome = 'paid') = (ledger_transaction_id IS NOT NULL)");
        });

        // Pay başına tek kapanış; tüketicinin idempotency kapısı da bu anahtar.
        builder.HasKey(c => c.HoldId).HasName("pk_card_topup_hold_closures");

        builder.Property(c => c.HoldId).HasColumnName("hold_id").ValueGeneratedNever();

        builder.Property(c => c.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("text")
            .HasConversion(Outcome);

        builder.Property(c => c.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(c => c.ClosedAt).HasColumnName("closed_at");

        builder.HasOne<CardTopupHold>()
            .WithOne()
            .HasForeignKey<CardTopupHoldClosure>(c => c.HoldId)
            .HasConstraintName("fk_card_topup_hold_closures_hold")
            .OnDelete(DeleteBehavior.Restrict);

        // Ledger işlemine FK YOK, processed_events'teki gibi: kapanış idempotency kapısı ve
        // ledger işleminden ÖNCE yazılıyor, aynı transaction'da.
    }
}

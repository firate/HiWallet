using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Configurations;

/// <summary>İadenin kalıcı hali; çekim saga'sının eşlemesiyle aynı kurallar.</summary>
internal sealed class DepositReturnSagaConfiguration : IEntityTypeConfiguration<DepositReturnSaga>
{
    public void Configure(EntityTypeBuilder<DepositReturnSaga> builder)
    {
        builder.ToTable("deposit_return_sagas");

        builder.HasKey(s => s.Id).HasName("pk_deposit_return_sagas");

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.SuspendedDepositId).HasColumnName("suspended_deposit_id");
        builder.Property(s => s.RequestedBy).HasColumnName("requested_by").HasColumnType("text").IsRequired();
        builder.Property(s => s.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text").IsRequired();

        builder.Property(s => s.State)
            .HasColumnName("state")
            .HasConversion(ValueConverters.DepositReturnState)
            .HasColumnType("text")
            .IsRequired();

        // Wallet düşene kadar NULL: tutarı ve bankayı orchestrator başlangıçta bilmiyor.
        builder.Property(s => s.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(s => s.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(s => s.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(s => s.DepositBankReference).HasColumnName("deposit_bank_reference").HasColumnType("text");

        builder.Property(s => s.DebitTransactionId).HasColumnName("debit_transaction_id");
        builder.Property(s => s.BankCommandId).HasColumnName("bank_command_id");
        builder.Property(s => s.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(s => s.BankFee).HasColumnName("bank_fee").HasColumnType("numeric(19,4)");
        builder.Property(s => s.SettlementTransactionId).HasColumnName("settlement_transaction_id");
        builder.Property(s => s.RestoreTransactionId).HasColumnName("restore_transaction_id");
        builder.Property(s => s.FailureReason).HasColumnName("failure_reason").HasColumnType("text");
        builder.Property(s => s.FailureRule).HasColumnName("failure_rule").HasColumnType("text");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Banka cevabı ile takılmış saga taraması aynı satıra gelebiliyor (madde 2).
        builder.Property(s => s.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        // Idempotency: kapsam havale. Aynı havale için başka anahtarla gelen ikinci iadeyi
        // wallet'ın kapısı reddediyor.
        builder.HasIndex(s => new { s.SuspendedDepositId, s.IdempotencyKey })
            .HasDatabaseName("ux_deposit_return_sagas_idempotency")
            .IsUnique();

        // Takılmış saga taraması: bitmemişler, en son ne zaman ilerledi.
        builder.HasIndex(s => s.UpdatedAt)
            .HasDatabaseName("ix_deposit_return_sagas_active")
            .HasFilter(ActiveStateFilter());
    }

    /// <summary>Durum listesinden üretiliyor; çekim saga'sındaki gerekçe.</summary>
    private static string ActiveStateFilter()
    {
        var states = DepositReturnStates.Active.Select(state => $"'{state.ToText()}'");

        return $"state IN ({string.Join(", ", states)})";
    }
}

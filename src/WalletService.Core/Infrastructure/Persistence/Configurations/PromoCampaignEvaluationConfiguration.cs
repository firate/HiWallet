using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class PromoCampaignEvaluationConfiguration : IEntityTypeConfiguration<PromoCampaignEvaluation>
{
    public void Configure(EntityTypeBuilder<PromoCampaignEvaluation> builder)
    {
        builder.ToTable("promo_campaign_evaluations");

        // Ödeme başına tek satır; tekrar değerlendirme PK'ya takılıyor.
        builder.HasKey(e => e.LedgerTransactionId).HasName("pk_promo_campaign_evaluations");

        builder.Property(e => e.LedgerTransactionId).HasColumnName("ledger_transaction_id");
        builder.Property(e => e.EvaluatedAt).HasColumnName("evaluated_at");

        // processed_events'ten farklı olarak FK var: her satır gerçek bir Payment işlemine ait.
        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(e => e.LedgerTransactionId)
            .HasConstraintName("fk_promo_campaign_evaluations_transaction")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

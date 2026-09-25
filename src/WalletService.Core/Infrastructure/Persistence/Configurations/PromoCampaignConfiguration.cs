using HiWallet.WalletService.Domain.Promos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Promo kampanyası (decisions.md madde 37). Backoffice gelene kadar SQL ile yazılıyor;
/// kurallar bu yüzden CHECK olarak da duruyor, elle yazılan satır da aynı kurallara takılıyor.
/// Şema: docs/ledger-schema.md "promo_campaigns".
/// </summary>
internal sealed class PromoCampaignConfiguration : IEntityTypeConfiguration<PromoCampaign>
{
    public void Configure(EntityTypeBuilder<PromoCampaign> builder)
    {
        builder.ToTable("promo_campaigns", t =>
        {
            t.HasCheckConstraint("ck_promo_campaigns_rule",
                "rule IN ('payment_to_merchant','daily_payment_total')");
            t.HasCheckConstraint("ck_promo_campaigns_reward_type", "reward_type IN ('fixed','percentage')");
            t.HasCheckConstraint("ck_promo_campaigns_grant_scope",
                "grant_scope IN ('all_businesses','selected_businesses')");

            t.HasCheckConstraint("ck_promo_campaigns_threshold",
                "(rule = 'daily_payment_total') = (threshold_amount IS NOT NULL) " +
                "AND (threshold_amount IS NULL OR threshold_amount > 0)");
            t.HasCheckConstraint("ck_promo_campaigns_percentage_rule",
                "rule = 'payment_to_merchant' OR reward_type = 'fixed'");
            t.HasCheckConstraint("ck_promo_campaigns_fixed",
                "(reward_type = 'fixed') = (reward_amount IS NOT NULL) " +
                "AND (reward_amount IS NULL OR reward_amount > 0)");
            t.HasCheckConstraint("ck_promo_campaigns_percentage",
                "(reward_type = 'percentage') = (reward_rate IS NOT NULL) " +
                "AND (reward_type = 'percentage') = (reward_max IS NOT NULL) " +
                "AND (reward_rate IS NULL OR (reward_rate > 0 AND reward_rate <= 1)) " +
                "AND (reward_max IS NULL OR reward_max > 0)");
            t.HasCheckConstraint("ck_promo_campaigns_limits",
                "budget > 0 AND daily_cap_per_account > 0 AND total_cap_per_account > 0");
            t.HasCheckConstraint("ck_promo_campaigns_period", "ends_at IS NULL OR ends_at > starts_at");
            t.HasCheckConstraint("ck_promo_campaigns_grant_valid_for",
                "grant_valid_for IS NULL OR grant_valid_for > interval '0'");
            t.HasCheckConstraint("ck_promo_campaigns_name", "btrim(name) <> ''");
        });

        builder.HasKey(c => c.Id).HasName("pk_promo_campaigns");

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Name).HasColumnName("name").HasColumnType("text").IsRequired();

        builder.Property(c => c.Rule)
            .HasColumnName("rule")
            .HasConversion(ValueConverters.PromoCampaignRule)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.ThresholdAmount).HasColumnName("threshold_amount").HasColumnType("numeric(19,4)");

        builder.Property(c => c.RewardType)
            .HasColumnName("reward_type")
            .HasConversion(ValueConverters.PromoRewardType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.RewardAmount).HasColumnName("reward_amount").HasColumnType("numeric(19,4)");

        // Oran para değil; ledger tutarları dışındaki oranlar serbest (decisions.md madde 19).
        builder.Property(c => c.RewardRate).HasColumnName("reward_rate").HasColumnType("numeric(9,6)");
        builder.Property(c => c.RewardMax).HasColumnName("reward_max").HasColumnType("numeric(19,4)");

        builder.Property(c => c.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(c => c.GrantScope)
            .HasColumnName("grant_scope")
            .HasConversion(ValueConverters.PromoScope)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.GrantValidFor).HasColumnName("grant_valid_for");

        builder.Property(c => c.Budget).HasColumnName("budget").HasColumnType("numeric(19,4)").IsRequired();
        builder.Property(c => c.DailyCapPerAccount)
            .HasColumnName("daily_cap_per_account").HasColumnType("numeric(19,4)").IsRequired();
        builder.Property(c => c.TotalCapPerAccount)
            .HasColumnName("total_cap_per_account").HasColumnType("numeric(19,4)").IsRequired();

        builder.Property(c => c.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(c => c.EndsAt).HasColumnName("ends_at");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(c => c.TriggerMerchants);
        builder.Ignore(c => c.ScopeMerchants);

        builder.HasMany(c => c.Merchants)
            .WithOne()
            .HasForeignKey(m => m.CampaignId)
            .HasConstraintName("fk_promo_campaign_merchants_campaign")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Merchants)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_merchants");
    }
}

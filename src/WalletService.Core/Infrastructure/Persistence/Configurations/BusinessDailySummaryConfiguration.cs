using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class BusinessDailySummaryConfiguration
    : IEntityTypeConfiguration<BusinessDailySummary>
{
    public void Configure(EntityTypeBuilder<BusinessDailySummary> builder)
    {
        builder.ToTable("business_daily_summaries");

        // Yüzey id YOK: satırı tanımlayan şey zaten bu üçlü. Ayrı bir id olsaydı
        // aynı gün için ikinci bir satır yazmak MÜMKÜN olurdu ve rapor sessizce
        // ikiye katlanırdı. Job'ın upsert'i de bu anahtara dayanıyor.
        builder.HasKey(s => new { s.AccountId, s.Day, s.Currency })
            .HasName("pk_business_daily_summaries");

        builder.Property(s => s.AccountId).HasColumnName("account_id");
        builder.Property(s => s.Day).HasColumnName("day").HasColumnType("date");

        builder.Property(s => s.Currency)
            .HasColumnName("currency")
            .HasColumnType("char(3)")
            .IsFixedLength();

        builder.Property(s => s.TransactionCount).HasColumnName("transaction_count");

        // Ledger ile AYNI tip: rapor tutarları ledger'dan toplanıyor, farklı bir
        // ölçekte tutmak yuvarlama farkı üretirdi.
        builder.Property(s => s.Volume).HasColumnName("volume").HasColumnType("numeric(19,4)");
        builder.Property(s => s.Commission).HasColumnName("commission").HasColumnType("numeric(19,4)");

        builder.Property(s => s.CalculatedAt).HasColumnName("calculated_at");

        // "Dün bütün işletmelerde ne oldu" sorgusu; hesap bazlı erişim zaten PK'nın
        // ilk kolonundan geliyor.
        builder.HasIndex(s => s.Day).HasDatabaseName("ix_business_daily_summaries_day");

        // accounts'a FK YOK: rapor türetilmiş veri ve hesabın yaşam döngüsüne
        // bağlanmamalı. Bir hesap silinirse (bugün silinmiyor) geçmiş raporun da
        // kaybolması istenmez — muhasebe kaydı değil ama iş kaydı.
    }
}

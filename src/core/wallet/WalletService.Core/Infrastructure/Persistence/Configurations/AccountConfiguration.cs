using HiWallet.WalletService.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>Müşteri hesabı. Şema: docs/ledger-schema.md "accounts".</summary>
internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", t =>
        {
            t.HasCheckConstraint("ck_accounts_type", "type IN ('person','business')");

            // Seviye yalnızca bireysel hesapta ve her bireysel hesapta var: işyeri
            // doğrulaması ayrı bir iş, seviyesiz bir bireysel hesap limitsiz kalırdı.
            t.HasCheckConstraint(
                "ck_accounts_kyc_level",
                "(type = 'person' AND kyc_level IN ('unknown','unverified','verified','contracted')) " +
                "OR (type = 'business' AND kyc_level IS NULL)");

            // İşyeri hesabının tek bir sahibi yok, kullanıcıları account_members'ta.
            t.HasCheckConstraint("ck_accounts_holder", "type = 'person' OR holder IS NULL");

            // Biçim: on hane, ilk hane sıfır değil. Kontrol hanesi uygulamada
            // (AccountNumber); SQL'e ikinci bir kopyası yazılmıyor.
            t.HasCheckConstraint("ck_accounts_number", "number ~ '^[1-9][0-9]{9}$'");
        });

        builder.HasKey(a => a.Id).HasName("pk_accounts");

        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.Number)
            .HasColumnName("number")
            .HasConversion(ValueConverters.AccountNumber)
            .HasColumnType("text")
            .IsRequired();

        // Numara tekil. Açılış çakışmada yeni numarayla yeniden deniyor.
        builder.HasIndex(a => a.Number)
            .HasDatabaseName("ux_accounts_number")
            .IsUnique();

        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion(ValueConverters.AccountType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.Holder)
            .HasColumnName("holder")
            .HasColumnType("text");

        builder.Property(a => a.KycLevel)
            .HasColumnName("kyc_level")
            .HasConversion(ValueConverters.KycLevel)
            .HasColumnType("text");

        // Kimlik başına tek bireysel hesap. Açılış bu index'e ON CONFLICT ile yazıyor;
        // tekrar eden ya da eşzamanlı açılış ikinci hesap üretmiyor. Sahibi olmayan eski
        // bireysel hesaplar (NULL) dışarıda.
        builder.HasIndex(a => a.Holder)
            .HasDatabaseName("ux_accounts_person_holder")
            .IsUnique()
            .HasFilter("holder IS NOT NULL");

        builder.Property(a => a.AcceptsPromo)
            .HasColumnName("accepts_promo")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
    }
}

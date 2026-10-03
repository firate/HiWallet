using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Hesabın para birimi başına varsayılan cüzdanı. Şema: docs/ledger-schema.md
/// "default_wallets".
///
/// Cüzdana işaret değil ayrı tablo: varsayılanı değiştirmek tek satırın güncellenmesi,
/// anahtar (hesap, para birimi) başına en fazla bir varsayılanı veritabanı garanti ediyor.
/// Cüzdanda bir işaret olsaydı değişiklik iki satır demekti ve araya giren bir okuma iki
/// varsayılan ya da hiç varsayılan görebilirdi.
/// </summary>
internal sealed class DefaultWalletConfiguration : IEntityTypeConfiguration<DefaultWallet>
{
    public void Configure(EntityTypeBuilder<DefaultWallet> builder)
    {
        builder.ToTable("default_wallets");

        builder.HasKey(d => new { d.AccountId, d.Currency }).HasName("pk_default_wallets");

        builder.Property(d => d.AccountId).HasColumnName("account_id");

        builder.Property(d => d.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(d => d.WalletId).HasColumnName("wallet_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(d => d.AccountId)
            .HasConstraintName("fk_default_wallets_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Para birimi cüzdanınkiyle aynı (decisions.md madde 17'deki composite FK hedefi).
        // Hesabın kendisiyle eşleşmesini DefaultWallet.Of sağlıyor: hesabı cüzdandan okuyor.
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(d => new { d.WalletId, d.Currency })
            .HasPrincipalKey(w => new { w.Id, w.Currency })
            .HasConstraintName("fk_default_wallets_wallet")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.WalletId, d.Currency }).HasDatabaseName("ix_default_wallets_wallet");
    }
}

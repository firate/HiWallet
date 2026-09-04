using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ledger'dan türetilmiş projeksiyon. Şema: docs/ledger-schema.md "wallet_balances".
///
/// Not: tablo adı "wallet" diyor ama sistem hesaplarının bakiyelerini de tutuyor —
/// mutabakat clearing bakiyesine bakıyor. Adı ledger_balances olmalı; yeniden
/// adlandırma yapılmadı, ayrı bir karar.
/// </summary>
internal sealed class WalletBalanceConfiguration : IEntityTypeConfiguration<WalletBalance>
{
    public void Configure(EntityTypeBuilder<WalletBalance> builder)
    {
        builder.ToTable("wallet_balances");

        builder.HasKey(b => b.LedgerAccountId).HasName("pk_wallet_balances");

        builder.Property(b => b.LedgerAccountId).HasColumnName("ledger_account_id");

        builder.Property(b => b.Balance)
            .HasColumnName("balance")
            .HasColumnType("numeric(19,4)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        // Projedeki TEK concurrency token (decisions.md madde 2). Elle artırılır,
        // DB üretmez — retry'da yeni anlık görüntüyle yeniden hesaplanması için.
        builder.Property(b => b.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.Ignore(b => b.Money);

        // ledger_entries ile aynı gerekçe: projeksiyonun para birimi hesabınkinden sapamaz.
        builder.HasOne<LedgerAccount>()
            .WithMany()
            .HasForeignKey(b => new { b.LedgerAccountId, b.Currency })
            .HasPrincipalKey(a => new { a.Id, a.Currency })
            .HasConstraintName("fk_wallet_balances_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK'nın index'i. PK zaten ledger_account_id üzerinde olduğu için bu
        // index fiilen gereksiz, ama EF composite FK'ya index üretmeden geçmiyor.
        // Adı verilmezse şemada PascalCase kalıyor.
        builder.HasIndex(b => new { b.LedgerAccountId, b.Currency })
            .HasDatabaseName("ix_wallet_balances_ledger_account_currency");
    }
}

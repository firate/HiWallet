using HiWallet.WalletService.Domain.Balances;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ledger'dan türetilmiş projeksiyon. Şema: docs/ledger-schema.md "ledger_balances".
///
/// Yalnızca cüzdanların değil TÜM ledger hesaplarının bakiyesi burada — mutabakat
/// clearing'e, rapor nostro'ya bakıyor. `ledger_accounts` ile 1:1 ama ayrı tablo:
/// orası neredeyse hiç yazılmayan referans verisi, burası her transfer'de yazılan
/// projeksiyon. Birleşselerdi her transfer geniş satırı ve onun unique index'lerini
/// güncellerdi; ayrıca projeksiyonu ledger'dan yeniden inşa etmek (TRUNCATE + replay)
/// mümkün olmazdı.
/// </summary>
internal sealed class LedgerBalanceConfiguration : IEntityTypeConfiguration<LedgerBalance>
{
    public void Configure(EntityTypeBuilder<LedgerBalance> builder)
    {
        builder.ToTable("ledger_balances");

        builder.HasKey(b => b.LedgerAccountId).HasName("pk_ledger_balances");

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

        // Wallet'taki TEK concurrency token (decisions.md madde 2). Elle artırılır,
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
            .HasConstraintName("fk_ledger_balances_ledger_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK'nın index'i. PK zaten ledger_account_id üzerinde olduğu için bu
        // index fiilen gereksiz, ama EF composite FK'ya index üretmeden geçmiyor.
        // Adı verilmezse şemada PascalCase kalıyor.
        builder.HasIndex(b => new { b.LedgerAccountId, b.Currency })
            .HasDatabaseName("ix_ledger_balances_ledger_account_currency");

        SeedSystemAccountBalances(builder);
    }

    /// <summary>
    /// Sistem hesaplarının bakiye satırları da seed ile gelir. Tembel yaratılsaydı ilk
    /// ledger yazımı satırı bulamaz ve mutabakat sorgusu (LEFT JOIN) o hesabı hiç
    /// göremezdi — sapma varsa görünmezdi.
    /// </summary>
    private static void SeedSystemAccountBalances(EntityTypeBuilder<LedgerBalance> builder)
    {
        var seed = SystemAccounts.All
            .Select(a => new
            {
                LedgerAccountId = a.Id,
                Balance = 0m,
                Currency = SystemAccounts.DefaultCurrency,
                Version = 0L,
                UpdatedAt = SystemAccounts.SeededAt
            })
            .ToArray();

        builder.HasData(seed);
    }
}

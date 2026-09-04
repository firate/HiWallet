using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Bakiye tutabilen her şey: cüzdanlar + sistem hesapları.
/// Şema: docs/ledger-schema.md "ledger_accounts".
/// </summary>
internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    /// <summary>
    /// <c>ux_ledger_accounts_system</c> index'i <c>COALESCE(provider,'')</c> üzerinde olmalı —
    /// unique index'te NULL'lar birbirine eşit sayılmadığı için ham kolon yetmiyor.
    /// EF ifade index'i modelleyemediğinden aynı sonucu STORED generated column ile alıyoruz:
    /// kolon türetilmiş, sapamaz, ve index EF modelinin parçası kalıyor (ham SQL yok).
    /// </summary>
    private const string ProviderKey = "ProviderKey";

    public void Configure(EntityTypeBuilder<LedgerAccount> builder)
    {
        builder.ToTable("ledger_accounts", t =>
        {
            t.HasCheckConstraint("ck_ledger_accounts_type",
                "type IN ('user_wallet','clearing','revenue','nostro','provider_expense')");

            // Kolon başına bir kural: "bu kolon TAM OLARAK şu tipte dolu".
            // İhlalde Postgres constraint adını söylüyor, hangi kuralın bozulduğu belli oluyor.
            t.HasCheckConstraint("ck_ledger_accounts_account",
                "(type = 'user_wallet') = (account_id IS NOT NULL)");
            t.HasCheckConstraint("ck_ledger_accounts_name",
                "(type = 'user_wallet') = (name IS NOT NULL)");
            t.HasCheckConstraint("ck_ledger_accounts_name_blank",
                "name IS NULL OR btrim(name) <> ''");
            t.HasCheckConstraint("ck_ledger_accounts_provider",
                "(type IN ('clearing','nostro','provider_expense')) = (provider IS NOT NULL)");
            t.HasCheckConstraint("ck_ledger_accounts_provider_blank",
                "provider IS NULL OR btrim(provider) <> ''");
        });

        builder.HasKey(a => a.Id).HasName("pk_ledger_accounts");

        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion(ValueConverters.LedgerAccountType)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(a => a.AccountId).HasColumnName("account_id");
        builder.Property(a => a.Name).HasColumnName("name").HasColumnType("text");
        builder.Property(a => a.Provider).HasColumnName("provider").HasColumnType("text");

        builder.Property(a => a.Currency)
            .HasColumnName("currency")
            .HasConversion(ValueConverters.Currency)
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");

        // Yalnızca index için var; Domain bu kolonu tanımaz (shadow property).
        builder.Property<string>(ProviderKey)
            .HasColumnName("provider_key")
            .HasColumnType("text")
            .HasComputedColumnSql("COALESCE(provider, '')", stored: true);

        // Tekillik amacı YOK: id zaten PK. Tek işi ledger_entries ve wallet_balances'ın
        // composite FK hedefi olabilmek (decisions.md madde 17).
        builder.HasAlternateKey(a => new { a.Id, a.Currency })
            .HasName("uq_ledger_accounts_id_currency");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(a => a.AccountId)
            .HasConstraintName("fk_ledger_accounts_account")
            .OnDelete(DeleteBehavior.Restrict);

        // Bir hesabın cüzdanlarını listelemek ve günlük limitini toplamak için.
        // Limit hesap bazında uygulandığından sıcak yolda (decisions.md madde 20).
        builder.HasIndex(a => a.AccountId, "ix_ledger_accounts_account")
            .HasFilter("account_id IS NOT NULL");

        // Sistem hesabı tekilliği: aynı (tip, sağlayıcı, currency) ikinci kez açılamaz.
        builder.HasIndex(nameof(LedgerAccount.Type), ProviderKey, nameof(LedgerAccount.Currency))
            .HasDatabaseName("ux_ledger_accounts_system")
            .IsUnique()
            .HasFilter("account_id IS NULL");

        // (account_id, currency) üzerinde tekillik YOK — bilinçli. Bir hesabın aynı para
        // biriminde birden fazla cüzdanı olabilir (decisions.md madde 20).

        SeedSystemAccounts(builder);
    }

    /// <summary>
    /// Sistem hesapları seed migration ile gelir (docs/ledger-schema.md "ledger_accounts").
    /// Anonim nesne kullanılıyor çünkü <see cref="LedgerAccount"/>'un public ctor'u yok —
    /// factory'ler zorunlu alanları doğruluyor, HasData ise doğrudan kolon değeri yazıyor.
    /// Kolonlar burada da eksiksiz verilmeli, aksi halde CHECK'lere takılır.
    /// </summary>
    private static void SeedSystemAccounts(EntityTypeBuilder<LedgerAccount> builder)
    {
        var seed = SystemAccounts.All
            .Select(a => new
            {
                Id = a.Id,
                Type = a.Type,
                AccountId = (Guid?)null,
                Name = (string?)null,
                Provider = a.Provider,
                Currency = SystemAccounts.DefaultCurrency,
                CreatedAt = SystemAccounts.SeededAt
            })
            .ToArray();

        builder.HasData(seed);
    }
}

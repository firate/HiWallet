using HiWallet.WalletService.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class ProviderFeeConfiguration : IEntityTypeConfiguration<ProviderFee>
{
    /// <summary>
    /// Şemadaki <c>CHECK (settlement_model IN ('net','invoiced'))</c> ile aynı
    /// değerler. Enum adı (<c>Net</c>) yazılsaydı yeniden adlandırma sessizce
    /// CHECK'i ihlal ederdi.
    /// </summary>
    private static readonly ValueConverter<FeeSettlement, string> SettlementModel =
        new(
            model => model == FeeSettlement.Net ? "net" : "invoiced",
            text => text == "net" ? FeeSettlement.Net : FeeSettlement.Invoiced);

    public void Configure(EntityTypeBuilder<ProviderFee> builder)
    {
        builder.ToTable("provider_fees", table =>
            table.HasCheckConstraint(
                "ck_provider_fees_settlement_model",
                "settlement_model IN ('net','invoiced')"));

        builder.HasKey(f => f.Id).HasName("pk_provider_fees");

        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.TransactionId).HasColumnName("transaction_id");
        builder.Property(f => f.Provider).HasColumnName("provider").HasColumnType("text");

        builder.Property(f => f.SettlementModel)
            .HasColumnName("settlement_model")
            .HasColumnType("text")
            .HasConversion(SettlementModel);

        builder.Property(f => f.FeeType).HasColumnName("fee_type").HasColumnType("text");

        // Ledger ile AYNI tip: fatura karşılaştırması bu iki tarafı topluyor, farklı
        // ölçek yuvarlama farkı üretir ve fark "sağlayıcı oranı değiştirmiş" gibi
        // görünürdü (madde 11).
        builder.Property(f => f.ExpectedAmount)
            .HasColumnName("expected_amount").HasColumnType("numeric(19,4)");

        builder.Property(f => f.ActualAmount)
            .HasColumnName("actual_amount").HasColumnType("numeric(19,4)");

        builder.Property(f => f.Currency)
            .HasColumnName("currency").HasColumnType("char(3)").IsFixedLength();

        builder.Property(f => f.ProviderRef).HasColumnName("provider_ref").HasColumnType("text");
        builder.Property(f => f.InvoiceRef).HasColumnName("invoice_ref").HasColumnType("text");
        builder.Property(f => f.LedgerTransactionId).HasColumnName("ledger_tx_id");
        builder.Property(f => f.OccurredAt).HasColumnName("occurred_at");
        builder.Property(f => f.Note).HasColumnName("note").HasColumnType("text");

        // "Bu sağlayıcıda faturalanmamış ne var" — fatura eşleştirmesinin (madde 11)
        // ve mutabakatın (5.7) sıcak sorgusu. Kısmi index: faturalanmış satırlar
        // hiç sorulmuyor.
        builder.HasIndex(f => new { f.Provider, f.OccurredAt })
            .HasDatabaseName("ix_provider_fees_unbilled")
            .HasFilter("invoice_ref IS NULL");

        builder.HasIndex(f => f.TransactionId).HasDatabaseName("ix_provider_fees_tx");

        // FK'lar ŞEMADA var (ledger-schema.md) ama EF navigation'ı AÇILMIYOR:
        // ilişkiyi gezinilebilir yapmak, ledger okuyan sorguların ücret tablosunu
        // yanlışlıkla yüklemesine kapı açardı. İki tablo bilerek ayrı duruyor.
        builder.HasIndex(f => f.InvoiceRef).HasDatabaseName("ix_provider_fees_invoice");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class ProviderInvoiceConfiguration : IEntityTypeConfiguration<ProviderInvoice>
{
    public void Configure(EntityTypeBuilder<ProviderInvoice> builder)
    {
        builder.ToTable("provider_invoices", table =>
            table.HasCheckConstraint(
                "ck_provider_invoices_status",
                "status IN ('applied','pending_review')"));

        builder.HasKey(i => i.Id).HasName("pk_provider_invoices");

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(i => i.InvoiceRef).HasColumnName("invoice_ref").HasColumnType("text");

        builder.Property(i => i.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");

        builder.Property(i => i.ExpectedAmount)
            .HasColumnName("expected_amount").HasColumnType("numeric(19,4)");

        builder.Property(i => i.Currency)
            .HasColumnName("currency").HasColumnType("char(3)").IsFixedLength();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .HasConversion(s => s.ToText(), text => ProviderInvoiceStatuses.FromText(text));

        builder.Property(i => i.FeeCount).HasColumnName("fee_count");
        builder.Property(i => i.LedgerTransactionId).HasColumnName("ledger_tx_id");
        builder.Property(i => i.ReceivedAt).HasColumnName("received_at");
        builder.Property(i => i.Note).HasColumnName("note").HasColumnType("text");

        // Aynı faturayı iki kez işlemek doğrudan yanlış gider kaydı (madde 11).
        // Idempotency'nin taşıyıcısı bu index; ledger tarafındaki
        // (ledger_account_id, idempotency_key) ikinci emniyet kemeri.
        builder.HasIndex(i => new { i.Provider, i.InvoiceRef })
            .HasDatabaseName("ux_provider_invoices_ref")
            .IsUnique();

        // "İncelenmeyi bekleyen ne var" — bu tablonun tek sıcak sorgusu. Kısmi
        // index: uygulanmış faturalar (zamanla tablonun tamamı) index'e girmiyor.
        builder.HasIndex(i => i.ReceivedAt)
            .HasDatabaseName("ix_provider_invoices_pending")
            .HasFilter("status = 'pending_review'");
    }
}

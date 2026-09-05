using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        // Bileşik PK, ayrı bir yüzey id yok: tekillik zaten bu ikilinin kendisi ve
        // idempotency tam olarak buna dayanıyor. event_id tek başına PK olsaydı iki
        // sağlayıcının aynı id'yi üretmesi birinin mesajını sessizce yutardı.
        builder.HasKey(e => new { e.Provider, e.EventId }).HasName("pk_processed_events");

        builder.Property(e => e.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(e => e.EventId).HasColumnName("event_id").HasColumnType("text");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.LedgerTransactionId).HasColumnName("ledger_transaction_id");

        // ledger_transactions'a FK YOK: kayıt ledger'a hiç yazılmadan da oluşabiliyor
        // (kolon NULL) ve FK bu tabloyu ledger'ın silinme davranışına bağlardı.
        // İlişki raporlamada join ile kuruluyor, şemada zorlanmıyor.
    }
}

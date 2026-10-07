using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WalletService.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        // Yüzey id YOK: tekillik zaten komutun kimliği ve idempotency tam olarak
        // buna dayanıyor.
        builder.HasKey(m => m.MessageId).HasName("pk_processed_messages");

        builder.Property(m => m.MessageId).HasColumnName("message_id");
        builder.Property(m => m.MessageType).HasColumnName("message_type").HasColumnType("text");
        builder.Property(m => m.SagaId).HasColumnName("saga_id");
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        builder.Property(m => m.LedgerTransactionId).HasColumnName("ledger_transaction_id");

        builder.Property(m => m.ReplyRoutingKey)
            .HasColumnName("reply_routing_key")
            .HasColumnType("text");

        // jsonb: teşhiste "bu saga'ya tam olarak ne cevap verdik" sorusu bunun
        // üzerinden cevaplanıyor.
        builder.Property(m => m.ReplyPayload).HasColumnName("reply_payload").HasColumnType("jsonb");

        // "Bu saga'ya hangi komutlar geldi" sorgusu.
        builder.HasIndex(m => m.SagaId).HasDatabaseName("ix_processed_messages_saga");

        // ledger_transactions'a FK YOK: ProcessedEvent ile aynı gerekçe — kayıt
        // ledger'a hiç yazılmadan da oluşuyor (reddedilen komut) ve FK bu tabloyu
        // ledger'ın silinme davranışına bağlardı.
    }
}

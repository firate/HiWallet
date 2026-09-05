using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("withdrawal_outbox");

        builder.HasKey(m => m.Id).HasName("pk_withdrawal_outbox");

        // Aynı zamanda komutun CommandId'si; gerekçe OutboxMessage.Id'de.
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.SagaId).HasColumnName("saga_id");

        builder.Property(m => m.RoutingKey)
            .HasColumnName("routing_key")
            .HasColumnType("text")
            .IsRequired();

        // jsonb: teşhis sırasında "bu saga bankaya hangi tutarı söyledi" sorusu
        // payload üzerinden cevaplanıyor. text olsaydı sorgulanamazdı.
        builder.Property(m => m.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.PublishedAt).HasColumnName("published_at");

        builder.Property(m => m.PublishAttempts)
            .HasColumnName("publish_attempts")
            .HasDefaultValue(0);

        builder.Property(m => m.LastError)
            .HasColumnName("last_error")
            .HasColumnType("text");

        // Relay'in sıcak sorgusu: yayınlanmamışlar, yazılma sırasına göre. Kısmi index,
        // topup_inbox'takiyle aynı gerekçe.
        builder.HasIndex(m => m.CreatedAt)
            .HasDatabaseName("ix_withdrawal_outbox_unpublished")
            .HasFilter("published_at IS NULL");

        // withdrawal_sagas'a FK YOK, bilerek. Outbox bir taşıma günlüğü, saga'nın
        // parçası değil: yayınlanmış satırlar bir saklama işiyle budanacak ve FK
        // budamayı saga'nın ömrüne bağlardı. İlişki teşhiste join ile kuruluyor.
        builder.HasIndex(m => m.SagaId).HasDatabaseName("ix_withdrawal_outbox_saga");
    }
}

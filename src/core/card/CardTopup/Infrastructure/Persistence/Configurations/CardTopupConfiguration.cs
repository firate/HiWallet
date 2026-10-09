using HiWallet.CardTopup.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.CardTopup.Infrastructure.Persistence.Configurations;

internal sealed class CardTopupConfiguration : IEntityTypeConfiguration<Domain.CardTopup>
{
    private static readonly ValueConverter<CardTopupState, string> State =
        new(state => state.ToText(), text => CardTopupStates.FromText(text));

    public void Configure(EntityTypeBuilder<Domain.CardTopup> builder)
    {
        builder.ToTable("card_topups", t =>
        {
            // Enum adı değil metin: yeniden adlandırma sessizce CHECK'i ihlal etmesin.
            t.HasCheckConstraint("ck_card_topups_state",
                "state IN ('created','pending','paid','failed','rejected')");
            t.HasCheckConstraint("ck_card_topups_amount", "amount > 0");
        });

        builder.HasKey(c => c.Id).HasName("pk_card_topups");

        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.Subject).HasColumnName("subject").HasColumnType("text");
        builder.Property(c => c.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text");
        builder.Property(c => c.WalletId).HasColumnName("wallet_id");
        builder.Property(c => c.AccountId).HasColumnName("account_id");
        builder.Property(c => c.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(c => c.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(c => c.Provider).HasColumnName("provider").HasColumnType("text");
        builder.Property(c => c.ReturnUrl).HasColumnName("return_url").HasColumnType("text");

        builder.Property(c => c.State)
            .HasColumnName("state")
            .HasColumnType("text")
            .HasConversion(State);

        builder.Property(c => c.PaymentId).HasColumnName("payment_id").HasColumnType("text");
        builder.Property(c => c.PaymentUrl).HasColumnName("payment_url").HasColumnType("text");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.FailureReason).HasColumnName("failure_reason").HasColumnType("text");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // Bu servisin TEK concurrency token'ı (decisions.md madde 2): sağlayıcının bildirimi
        // ile tarama aynı yüklemeye aynı anda gelebiliyor. Token elle artırılıyor.
        builder.Property(c => c.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(c => c.IsTerminal);

        // Idempotency (CLAUDE.md "Idempotency"): kapsam isteyen kimlik.
        builder.HasIndex(c => new { c.Subject, c.IdempotencyKey })
            .HasDatabaseName("ux_card_topups_idempotency")
            .IsUnique();

        // Taramanın sıcak sorgusu: bitmemişler, oturumun bitişine göre. Kısmi: bitmiş
        // yüklemeler (zamanla tablonun neredeyse tamamı) index'e girmiyor.
        builder.HasIndex(c => c.ExpiresAt)
            .HasDatabaseName("ix_card_topups_open")
            .HasFilter("state IN ('created','pending')");

        // Cüzdanın yükleme geçmişi, yeniden eskiye.
        builder.HasIndex(c => new { c.WalletId, c.CreatedAt })
            .HasDatabaseName("ix_card_topups_wallet");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("card_topup_outbox");

        builder.HasKey(m => m.Id).HasName("pk_card_topup_outbox");

        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(m => m.CardTopupId).HasColumnName("card_topup_id");

        // jsonb: teşhiste "wallet'a ne söylendi" sorgulanabilsin.
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.PublishedAt).HasColumnName("published_at");
        builder.Property(m => m.PublishAttempts).HasColumnName("publish_attempts").HasDefaultValue(0);
        builder.Property(m => m.LastError).HasColumnName("last_error").HasColumnType("text");

        // Relay'in sıcak sorgusu: yayınlanmamışlar, yazılma sırasına göre.
        builder.HasIndex(m => m.CreatedAt)
            .HasDatabaseName("ix_card_topup_outbox_unpublished")
            .HasFilter("published_at IS NULL");

        // card_topups'a FK YOK, çekim outbox'ındaki gerekçeyle: outbox bir taşıma günlüğü.
        builder.HasIndex(m => m.CardTopupId).HasDatabaseName("ix_card_topup_outbox_card_topup");
    }
}

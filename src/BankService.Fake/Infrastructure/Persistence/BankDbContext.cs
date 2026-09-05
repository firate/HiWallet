using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.BankService.Infrastructure.Persistence;

/// <summary>
/// Sahte bankanın kendi veritabanı (<c>hiwallet_bank</c>). Wallet ve orchestrator
/// şemalarına DOKUNMUYOR — gerçek bir banka da onları göremezdi.
///
/// Tek rol: append-only zorlanacak tablosu yok.
/// </summary>
public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    internal DbSet<BankTransfer> Transfers => Set<BankTransfer>();

    internal DbSet<TransferScenario> Scenarios => Set<TransferScenario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BankTransferConfiguration());
        modelBuilder.ApplyConfiguration(new TransferScenarioConfiguration());
    }
}

internal sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> builder)
    {
        builder.ToTable("bank_transfers");

        // Yüzey id YOK: tekillik komutun kimliği ve idempotency tam olarak buna
        // dayanıyor (decisions.md madde 32).
        builder.HasKey(t => t.CommandId).HasName("pk_bank_transfers");

        builder.Property(t => t.CommandId).HasColumnName("command_id");
        builder.Property(t => t.SagaId).HasColumnName("saga_id");
        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(t => t.Currency).HasColumnName("currency").HasColumnType("char(3)");

        builder.Property(t => t.DestinationIban)
            .HasColumnName("destination_iban")
            .HasColumnType("text");

        builder.Property(t => t.Outcome).HasColumnName("outcome").HasColumnType("text");
        builder.Property(t => t.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(t => t.FailureReason).HasColumnName("failure_reason").HasColumnType("text");
        builder.Property(t => t.Attempts).HasColumnName("attempts");
        builder.Property(t => t.ProcessedAt).HasColumnName("processed_at");

        builder.Property(t => t.ReplyRoutingKey)
            .HasColumnName("reply_routing_key")
            .HasColumnType("text");

        builder.Property(t => t.ReplyPayload).HasColumnName("reply_payload").HasColumnType("jsonb");

        // "Bu saga için ne yaptık" sorgusu. Bir saga'nın birden fazla komutu
        // olabiliyor (yeniden gönderim), o yüzden unique DEĞİL.
        builder.HasIndex(t => t.SagaId).HasDatabaseName("ix_bank_transfers_saga");
    }
}

internal sealed class TransferScenarioConfiguration : IEntityTypeConfiguration<TransferScenario>
{
    public void Configure(EntityTypeBuilder<TransferScenario> builder)
    {
        builder.ToTable("transfer_scenarios");

        builder.HasKey(s => s.SagaId).HasName("pk_transfer_scenarios");

        builder.Property(s => s.SagaId).HasColumnName("saga_id");
        builder.Property(s => s.Outcome).HasColumnName("outcome").HasColumnType("text");

        builder.Property(s => s.RemainingTransientFailures)
            .HasColumnName("remaining_transient_failures")
            .HasDefaultValue(0);

        builder.Property(s => s.DelayMilliseconds)
            .HasColumnName("delay_milliseconds")
            .HasDefaultValue(0);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}

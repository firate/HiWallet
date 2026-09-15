using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Bank.Fake.Infrastructure.Persistence;

/// <summary>
/// Sahte bankanın KENDİ veritabanı (<c>hiwallet_bank_fake</c>). Adaptörün
/// <c>hiwallet_bank</c>'ıyla hiçbir ortaklığı yok — decisions.md madde 35.
/// </summary>
/// <remarks>
/// Sınıf public, <c>DbSet</c>'ler internal: DI ve <c>dotnet ef</c> tipi görmek
/// zorunda, ama tablolara yalnızca bu assembly'nin içinden erişiliyor.
/// </remarks>
public sealed class BankFakeDbContext(DbContextOptions<BankFakeDbContext> options)
    : DbContext(options)
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
        builder.ToTable("transfers");

        builder.HasKey(t => t.BankReference).HasName("pk_transfers");

        builder.Property(t => t.BankReference).HasColumnName("bank_reference").HasColumnType("text");
        builder.Property(t => t.ClientReference).HasColumnName("client_reference").HasColumnType("text");
        builder.Property(t => t.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text");

        // Para tipi wallet'takiyle aynı: numeric(19,4). Sahte de olsa float kullanmak
        // sonucu bizim tarafta yuvarlama farkı olarak gösterirdi.
        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
        builder.Property(t => t.Fee).HasColumnName("fee").HasColumnType("numeric(19,4)");

        builder.Property(t => t.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(t => t.DestinationIban).HasColumnName("destination_iban").HasColumnType("text");
        builder.Property(t => t.Outcome).HasColumnName("outcome").HasColumnType("text");

        builder.Property(t => t.ResolveAt).HasColumnName("resolve_at");
        builder.Property(t => t.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(t => t.CallbackSentAt).HasColumnName("callback_sent_at");
        builder.Property(t => t.CallbackAttempts).HasColumnName("callback_attempts").HasDefaultValue(0);
        builder.Property(t => t.LastCallbackError).HasColumnName("last_callback_error").HasColumnType("text");

        // Bankanın kendi idempotency koruması: aynı anahtarla ikinci istek yeni
        // transfer AÇMIYOR. Partial değil — anahtar her satırda dolu.
        builder.HasIndex(t => t.IdempotencyKey)
            .HasDatabaseName("ux_transfers_idempotency")
            .IsUnique();

        // Callback göndericisinin sıcak sorgusu: "sonucu belli olmuş ama callback'i
        // gitmemiş satırlar". Kısmi index, gönderilmiş satırlar taranmasın diye.
        builder.HasIndex(t => t.ResolveAt)
            .HasDatabaseName("ix_transfers_pending_callback")
            .HasFilter("callback_sent_at IS NULL");
    }
}

internal sealed class TransferScenarioConfiguration : IEntityTypeConfiguration<TransferScenario>
{
    public void Configure(EntityTypeBuilder<TransferScenario> builder)
    {
        builder.ToTable("transfer_scenarios");

        builder.HasKey(s => s.ClientReference).HasName("pk_transfer_scenarios");

        builder.Property(s => s.ClientReference).HasColumnName("client_reference").HasColumnType("text");
        builder.Property(s => s.Outcome).HasColumnName("outcome").HasColumnType("text");

        builder.Property(s => s.RemainingTransientFailures)
            .HasColumnName("remaining_transient_failures")
            .HasDefaultValue(0);

        builder.Property(s => s.DelayMilliseconds)
            .HasColumnName("delay_milliseconds")
            .HasDefaultValue(0);

        // Eski şemada bu kolona HasColumnName verilmemişti ve EF "Attempts" diye
        // PascalCase bir kolon üretmişti — Postgres'te çift tırnaksız erişilemeyen
        // bir kolon. Şema genelinde snake_case olduğu için burada açıkça veriliyor.
        builder.Property(s => s.Attempts).HasColumnName("attempts").HasDefaultValue(0);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}

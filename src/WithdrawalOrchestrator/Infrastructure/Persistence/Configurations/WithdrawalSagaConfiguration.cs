using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence.Configurations;

/// <summary>
/// Saga'nın kalıcı hali. Domain sınıfı EF'i bilmiyor — eşleme burada, tersi değil
/// (structure.md "Bağımlılık yönü").
/// </summary>
internal sealed class WithdrawalSagaConfiguration : IEntityTypeConfiguration<WithdrawalSaga>
{
    public void Configure(EntityTypeBuilder<WithdrawalSaga> builder)
    {
        builder.ToTable("withdrawal_sagas");

        builder.HasKey(s => s.Id).HasName("pk_withdrawal_sagas");

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.AccountId).HasColumnName("account_id");
        builder.Property(s => s.WalletId).HasColumnName("wallet_id");

        // Çekimi kim istedi (decisions.md madde 34). account_id "parası kimin"
        // sorusunu cevaplıyor, bunlar "kim istedi" sorusunu — backoffice müşteri
        // adına çekim açtığında ikisi ayrışıyor.
        builder.Property(s => s.InitiatedByType)
            .HasColumnName("initiated_by_type").HasColumnType("text").IsRequired();

        builder.Property(s => s.InitiatedById)
            .HasColumnName("initiated_by_id").HasColumnType("text").IsRequired();

        builder.Property(s => s.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasColumnType("text")
            .IsRequired();

        // Müşterinin istediği tutar, komisyon HARİÇ. Ledger'la aynı tip: orchestrator
        // toplama yapmıyor ama iki tarafın aynı sayıyı aynı hassasiyette tutması
        // mutabakatta karşılaştırmayı mümkün kılıyor.
        builder.Property(s => s.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(19,4)")
            .IsRequired();

        // Ledger'daki `currency` ile aynı tip. Burada Currency değer nesnesi YOK:
        // o wallet sınırının içinde (CLAUDE.md "Servis sınırı").
        builder.Property(s => s.Currency)
            .HasColumnName("currency")
            .HasColumnType("char(3)")
            .IsRequired();

        builder.Property(s => s.Destination)
            .HasColumnName("destination_iban")
            .HasConversion(ValueConverters.Iban)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(s => s.State)
            .HasColumnName("state")
            .HasConversion(ValueConverters.WithdrawalState)
            .HasColumnType("text")
            .IsRequired();

        // Cüzdandan gerçekte çıkan toplam. Reddedilen saga'da NULL kalıyor —
        // 0 yazmak "komisyonsuz çekim yapıldı" ile ayırt edilemezdi.
        builder.Property(s => s.TotalDebited)
            .HasColumnName("total_debited")
            .HasColumnType("numeric(19,4)");

        builder.Property(s => s.DebitTransactionId).HasColumnName("debit_transaction_id");
        builder.Property(s => s.RefundTransactionId).HasColumnName("refund_transaction_id");
        builder.Property(s => s.BankCommandId).HasColumnName("bank_command_id");

        builder.Property(s => s.BankReference).HasColumnName("bank_reference").HasColumnType("text");

        // Ledger ile AYNI tip: settlement komutunda taşınıp wallet'ta ledger'a
        // yazılıyor, farklı ölçek yuvarlama farkı üretirdi.
        builder.Property(s => s.BankFee).HasColumnName("bank_fee").HasColumnType("numeric(19,4)");

        builder.Property(s => s.SettlementTransactionId).HasColumnName("settlement_transaction_id");

        builder.Property(s => s.FailureReason)
            .HasColumnName("failure_reason")
            .HasColumnType("text");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Orchestrator'ın TEK concurrency token'ı (decisions.md madde 2). Aynı saga'ya
        // banka cevabı ile takılmış saga taraması aynı anda gelebiliyor; ikisi de
        // "oku, karar ver, yaz" yapıyor. Token elle artırılıyor (state machine'deki
        // Advance), DB üretmiyor — retry'da geçişin yeni durum üstünde yeniden
        // değerlendirilmesi gerekiyor.
        builder.Property(s => s.Version)
            .HasColumnName("version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken()
            .IsRequired();

        // Idempotency (CLAUDE.md "Idempotency"). Yalnız key UNIQUE olsaydı iki
        // müşterinin aynı key'i üretmesi isteklerini karıştırırdı; kapsam hesap.
        builder.HasIndex(s => new { s.AccountId, s.IdempotencyKey })
            .HasDatabaseName("ux_withdrawal_sagas_idempotency")
            .IsUnique();

        // Takılmış saga taramasının sıcak sorgusu: bitmemişler, en son ne zaman
        // ilerledi sırasına göre. Kısmi index — bitmiş saga'lar (zamanla tablonun
        // neredeyse tamamı) index'e hiç girmiyor.
        builder.HasIndex(s => s.UpdatedAt)
            .HasDatabaseName("ix_withdrawal_sagas_active")
            .HasFilter(ActiveStateFilter());

        // Cüzdan bazında sorgular: müşterinin çekim geçmişi, aynı cüzdanda bekleyen
        // başka bir saga var mı.
        builder.HasIndex(s => new { s.WalletId, s.CreatedAt })
            .HasDatabaseName("ix_withdrawal_sagas_wallet");
    }

    /// <summary>
    /// Filtre durum listesinden ÜRETİLİYOR, elle yazılmıyor. Yeni bir durum
    /// eklendiğinde index'in filtresi de değişiyor ve migration bunu diff olarak
    /// görüyor — elle yazılsaydı sessizce eskirdi.
    /// </summary>
    private static string ActiveStateFilter()
    {
        var states = WithdrawalStates.Active.Select(state => $"'{state.ToText()}'");

        return $"state IN ({string.Join(", ", states)})";
    }
}

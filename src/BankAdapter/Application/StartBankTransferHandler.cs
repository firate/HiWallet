using HiWallet.BankIntegration.Domain;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Orchestrator'ın <c>StartBankTransfer</c> komutunu bankaya HTTP çağrısına çevirir.
///
/// <b>CEVAP ÜRETMİYOR.</b> Banka "aldım" diyor, biz satırı <c>pending</c> yazıp
/// mesajı ack'liyoruz ve saga gerçekten <c>bank_transfer_pending</c>'de kalıyor.
/// Sonuç sonra geliyor (decisions.md madde 35).
///
/// Eski sahte servis sonucu aynı teslimde dönüyordu; o yüzden bu durum fiilen ölüydü
/// ve takılmış saga taraması hiç iş yapmıyordu.
/// </summary>
internal sealed class StartBankTransferHandler(
    IDbContextFactory<BankDbContext> contextFactory,
    BankClient bank,
    TimeProvider timeProvider,
    ILogger<StartBankTransferHandler> logger)
{
    public async Task HandleAsync(StartBankTransfer command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Transfers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CommandId == command.CommandId, ct);

        if (existing is not null)
        {
            // Komut ikinci kez teslim edildi. Transfer TEKRARLANMIYOR. Cevap da
            // buradan yayınlanmıyor: kapanmış bir transferin cevabı zaten satırda
            // duruyor ve relay onu yayınlamakla görevli. Burada yayınlasaydık aynı
            // cevabın iki ayrı üreticisi olurdu.
            logger.LogInformation(
                "Komut zaten işlenmiş, transfer tekrarlanmıyor. Saga {SagaId}, durum {Status}",
                command.SagaId, existing.Status);

            return;
        }

        // ÖNCE BANKA, SONRA KAYIT. Ters sıra tehlikeli: satırı 'pending' yazıp
        // bankayı aramadan ölürsek, tekrar teslimde yukarıdaki dedup "zaten işlendi"
        // der ve banka HİÇ ARANMAZ — saga sonsuza kadar bekler.
        //
        // Bu sırada süreç bankayı aradıktan sonra ölürse kayıp yok: tekrar teslimde
        // aynı Idempotency-Key ile arıyoruz, banka yeni transfer açmıyor ve aynı
        // referansı geri veriyor.
        var accepted = await bank.StartTransferAsync(
            new StartTransferRequest(
                command.SagaId.ToString(),
                command.Amount,
                command.Currency,
                command.DestinationIban),
            command.CommandId,
            ct);

        var transfer = new BankTransfer
        {
            CommandId = command.CommandId,
            SagaId = command.SagaId,
            Amount = command.Amount,
            Currency = command.Currency,
            DestinationIban = command.DestinationIban,
            Status = BankTransferStatus.Pending.ToText(),
            BankReference = accepted.BankReference,
            StartedAt = timeProvider.GetUtcNow()
        };

        db.Transfers.Add(transfer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı komut iki kez paralel teslim edildi ve öbür taraf önce yazdı.
            // Banka tarafında sorun yok — iki çağrı da aynı Idempotency-Key'i
            // taşıdığı için tek transfer açıldı.
            logger.LogInformation(
                "Komut paralel işlendi, kayıt başkası tarafından yazılmış. Saga {SagaId}", command.SagaId);

            return;
        }

        logger.LogInformation(
            "Transfer bankaya iletildi. Saga {SagaId} → {BankReference}, {Amount} {Currency} → {Iban}",
            command.SagaId, accepted.BankReference, command.Amount, command.Currency,
            Mask(command.DestinationIban));
    }

    /// <summary>
    /// IBAN log'a maskeli yazılıyor. Kolonda tam duruyor — ihtilafta lazım — ama
    /// log'lar merkezi bir sisteme akıyor ve oradaki erişim çevresi daha geniş.
    /// </summary>
    private static string Mask(string iban) =>
        iban.Length <= 8 ? new string('*', iban.Length) : $"{iban[..4]}{new string('*', iban.Length - 8)}{iban[^4..]}";
}

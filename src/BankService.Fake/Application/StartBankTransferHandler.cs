using HiWallet.BankService.Infrastructure.Persistence;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankService.Application;

/// <summary>
/// Banka transferini "yapar" ve sonucu döner.
///
/// Gerçek bir bankada burada bir HTTP çağrısı olurdu; burada sonucu senaryo tablosu
/// belirliyor (overview.md madde 9). Etrafındaki her şey — idempotency, cevabın
/// saklanması, geçici hatada yeniden deneme — gerçek entegrasyonda da aynı olurdu.
/// Sahte olan yalnızca çağrının kendisi.
///
/// <b>Wallet'ı görmüyor.</b> Bu servisin ledger'dan, komisyondan, saga durumundan
/// haberi yok. Bildiği tek şey "şu tutarı şu IBAN'a gönder".
/// </summary>
public sealed class StartBankTransferHandler(
    IDbContextFactory<BankDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<StartBankTransferHandler> logger)
{
    public async Task<BankReply> HandleAsync(StartBankTransfer command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Daha önce işlendiyse transfer TEKRARLANMIYOR. Bankanın idempotency
        // anahtarı CommandId; aynı komut iki kez gelirse para iki kez gitmemeli.
        var stored = await db.Transfers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CommandId == command.CommandId, ct);

        if (stored is not null)
        {
            logger.LogInformation(
                "Transfer komutu zaten işlenmiş, saklanan cevap dönülüyor. Saga {SagaId}",
                command.SagaId);

            return new BankReply(stored.ReplyRoutingKey, stored.ReplyPayload, Replayed: true);
        }

        var scenario = await db.Scenarios.FirstOrDefaultAsync(s => s.SagaId == command.SagaId, ct);
        var outcome = ResolveOutcome(scenario);

        if (scenario is not null)
        {
            scenario.Attempts++;
        }

        if (outcome is TransferOutcome.TransientFailure)
        {
            // Sayaçlar azalıp COMMIT ediliyor: bir sonraki teslim bir eksiğini
            // görsün. Bellekte tutulsalardı süreç yeniden başladığında senaryo başa
            // döner ve "üç denemede başarılı" testi sonsuza kadar koşardı.
            scenario!.RemainingTransientFailures--;
            await db.SaveChangesAsync(ct);

            throw new TransientBankFailureException(
                command.SagaId, scenario.RemainingTransientFailures);
        }

        if (outcome is TransferOutcome.DelayedSuccess && scenario is { DelayMilliseconds: > 0 })
        {
            // Eventual davranışı görünür kılmak için. Kanal bu sırada bloke:
            // prefetch=1 ile başka mesaj gelmiyor, gerçek bir bankada da yavaş
            // çağrı aynı etkiyi yapardı.
            await Task.Delay(
                TimeSpan.FromMilliseconds(scenario.DelayMilliseconds), timeProvider, ct);
        }

        var reply = outcome is TransferOutcome.PermanentFailure
            ? Fail(command)
            : Succeed(command);

        db.Transfers.Add(new BankTransfer
        {
            CommandId = command.CommandId,
            SagaId = command.SagaId,
            Amount = command.Amount,
            Currency = command.Currency,
            DestinationIban = command.DestinationIban,
            Outcome = outcome.ToString(),
            BankReference = reply.RoutingKey == nameof(BankTransferSucceeded)
                ? BankReference(command)
                : null,
            FailureReason = reply.RoutingKey == nameof(BankTransferFailed) ? FailureReason : null,
            Attempts = scenario?.Attempts ?? 1,
            ProcessedAt = timeProvider.GetUtcNow(),
            ReplyRoutingKey = reply.RoutingKey,
            ReplyPayload = reply.Payload
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Transfer {Outcome}. Saga {SagaId}, {Amount} {Currency} → {Iban}",
            outcome, command.SagaId, command.Amount, command.Currency,
            Mask(command.DestinationIban));

        return reply;
    }

    private const string FailureReason = "Alıcı hesap kapalı.";

    private static TransferOutcome ResolveOutcome(TransferScenario? scenario)
    {
        // Senaryo kurulmamışsa başarı. Varsayılanın "başarı" olması testlerin
        // yalnızca ilgilendikleri sapmayı kurmasını sağlıyor.
        if (scenario is null) return TransferOutcome.Success;

        var configured = Enum.Parse<TransferOutcome>(scenario.Outcome);

        // Geçici hata kotası dolduysa senaryo başarıya dönüyor: "transient sonra
        // başarılı" (overview.md madde 9) tam olarak bu.
        if (configured is TransferOutcome.TransientFailure && scenario.RemainingTransientFailures <= 0)
        {
            return TransferOutcome.Success;
        }

        return configured;
    }

    private static BankReply Succeed(StartBankTransfer command) =>
        BankReply.For(new BankTransferSucceeded
        {
            SagaId = command.SagaId,
            BankReference = BankReference(command)
        });

    private static BankReply Fail(StartBankTransfer command) =>
        BankReply.For(new BankTransferFailed
        {
            SagaId = command.SagaId,
            Reason = FailureReason
        });

    /// <summary>
    /// Banka referansı komuttan TÜRETİLİYOR, rastgele değil. Aynı komut yeniden
    /// işlenirse aynı referans çıkıyor — gerçek bir bankada da idempotency anahtarı
    /// aynı transferi işaret eder.
    /// </summary>
    private static string BankReference(StartBankTransfer command) =>
        $"BNK-{command.CommandId.ToString("N")[..12].ToUpperInvariant()}";

    /// <summary>Log'a tam IBAN düşmesin; son dört hane teşhis için yeterli.</summary>
    private static string Mask(string iban) =>
        iban.Length <= 8 ? "****" : string.Concat(iban[..4], "***", iban[^4..]);
}

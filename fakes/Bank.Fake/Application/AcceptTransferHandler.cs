using HiWallet.Bank.Fake.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Transferi KABUL EDER — yapmaz. Bankanın bu çağrıya verdiği söz "aldım", "gönderdim"
/// değil; kesin sonuç sonra, callback ya da durum sorgusuyla öğreniliyor
/// (decisions.md madde 35).
///
/// Eski sahte servis sonucu aynı çağrıda dönüyordu ve bu yüzden bizim tarafta
/// <c>bank_transfer_pending</c> hiç beklenmeyen bir durumdu. Gerçek havale böyle
/// çalışmıyor.
/// </summary>
public sealed class AcceptTransferHandler(
    IDbContextFactory<BankFakeDbContext> contextFactory,
    IOptions<BankFakeOptions> options,
    TimeProvider timeProvider,
    ILogger<AcceptTransferHandler> logger)
{
    private readonly BankFakeOptions _options = options.Value;

    public async Task<AcceptResult> HandleAsync(
        AcceptTransferCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Bankanın KENDİ idempotency koruması. Bizim tarafımızdaki dedup'tan
        // bağımsız: gerçek entegrasyonda karşı tarafın koruma yaptığına güvenilmez,
        // iki taraf da kendi kaydını tutar.
        var existing = await db.Transfers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == command.IdempotencyKey, ct);

        if (existing is not null)
        {
            return Accepted(existing, replayed: true);
        }

        var scenario = await db.Scenarios
            .FirstOrDefaultAsync(s => s.ClientReference == command.ClientReference, ct)
            ?? MaterializeDefault(db, command.ClientReference);

        var outcome = ResolveOutcome(scenario);

        if (scenario is not null)
        {
            scenario.Attempts++;
        }

        if (outcome is TransferOutcome.TransientFailure)
        {
            // Sayaç azaltılıp COMMIT ediliyor: bir sonraki deneme bir eksiğini görsün.
            // Bellekte tutulsaydı süreç yeniden başladığında senaryo başa döner ve
            // "üç denemede başarılı" testi sonsuza kadar koşardı.
            scenario!.RemainingTransientFailures--;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Banka şu an cevap veremiyor. Client ref {ClientReference}, kalan {Remaining}",
                command.ClientReference, scenario.RemainingTransientFailures);

            return AcceptResult.TemporarilyUnavailable();
        }

        var now = timeProvider.GetUtcNow();

        var delay = outcome is TransferOutcome.DelayedSuccess && scenario is { DelayMilliseconds: > 0 }
            ? TimeSpan.FromMilliseconds(scenario.DelayMilliseconds)
            : _options.SettlementDelay;

        var transfer = new BankTransfer
        {
            BankReference = NewBankReference(),
            ClientReference = command.ClientReference,
            IdempotencyKey = command.IdempotencyKey,
            Amount = command.Amount,
            Currency = command.Currency,
            DestinationIban = command.DestinationIban,
            Outcome = outcome.ToText(),
            // Sonuç anında hazır olsa bile bir gecikme var: asenkron yapının tek
            // gözlemlenebilir tarafı bu pencere.
            ResolveAt = now + delay,
            Fee = outcome is TransferOutcome.Failure ? 0m : _options.TransferFee,
            AcceptedAt = now
        };

        db.Transfers.Add(transfer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anahtarla iki istek aynı anda geldi. "Önce SELECT sonra INSERT"
            // bu yarışı kapatmıyor; kapatan şey UNIQUE index. Kaybeden taraf
            // kazananın satırını okuyup onu dönüyor (CLAUDE.md "Idempotency").
            await using var retry = await contextFactory.CreateDbContextAsync(ct);

            var winner = await retry.Transfers
                .AsNoTracking()
                .FirstAsync(t => t.IdempotencyKey == command.IdempotencyKey, ct);

            return Accepted(winner, replayed: true);
        }

        logger.LogInformation(
            "Transfer kabul edildi. {BankReference} ← client ref {ClientReference}, sonuç {Outcome} {ResolveAt}",
            transfer.BankReference, transfer.ClientReference, transfer.Outcome, transfer.ResolveAt);

        return Accepted(transfer, replayed: false);
    }

    private AcceptResult Accepted(BankTransfer transfer, bool replayed) =>
        AcceptResult.Ok(
            transfer.BankReference,
            TransferResolution.StatusOf(transfer, timeProvider.GetUtcNow()),
            replayed);

    /// <summary>
    /// Bankanın kendi referansı. Bizim ürettiğimiz hiçbir kimlikten TÜRETİLMİYOR —
    /// gerçek bankada da öyle: referansı onlar üretiyor ve biz onu saklıyoruz.
    /// Türetseydik, testte "referansı nereden aldık" sorusu cevapsız kalırdı.
    /// </summary>
    private static string NewBankReference() => $"BNK{Guid.NewGuid():N}"[..19].ToUpperInvariant();

    /// <summary>
    /// Senaryosu olmayan çekim için varsayılan davranışı satıra dönüştürür.
    /// Varsayılan başarıysa satır AÇILMIYOR — tablo gereksiz yere şişmesin.
    ///
    /// Satır olarak yazılması şart: geçici hata sayacı ve deneme sayısı kalıcı
    /// olmadan "üç denemede başarılı" davranışı yeniden başlatmada başa dönerdi.
    /// </summary>
    private TransferScenario? MaterializeDefault(BankFakeDbContext db, string clientReference)
    {
        if (_options.DefaultOutcome is TransferOutcome.Success) return null;

        var scenario = new TransferScenario
        {
            ClientReference = clientReference,
            Outcome = _options.DefaultOutcome.ToText(),
            RemainingTransientFailures =
                _options.DefaultOutcome is TransferOutcome.TransientFailure
                    ? _options.DefaultTransientFailures
                    : 0,
            DelayMilliseconds = 0,
            Attempts = 0,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.Scenarios.Add(scenario);

        return scenario;
    }

    private static TransferOutcome ResolveOutcome(TransferScenario? scenario)
    {
        // Senaryo kurulmamışsa başarı. Varsayılanın "başarı" olması testlerin
        // yalnızca ilgilendikleri sapmayı kurmasını sağlıyor.
        if (scenario is null) return TransferOutcome.Success;

        var configured = TransferOutcomes.FromText(scenario.Outcome);

        // Geçici hata kotası dolduysa senaryo başarıya dönüyor: "transient sonra
        // başarılı" tam olarak bu.
        if (configured is TransferOutcome.TransientFailure && scenario.RemainingTransientFailures <= 0)
        {
            return TransferOutcome.Success;
        }

        return configured;
    }
}

/// <param name="ClientReference">Müşterinin kendi referansı; bizde saga kimliği.</param>
/// <param name="IdempotencyKey">Müşterinin idempotency anahtarı; bizde <c>CommandId</c>.</param>
public sealed record AcceptTransferCommand(
    string ClientReference,
    string IdempotencyKey,
    decimal Amount,
    string Currency,
    string DestinationIban);

/// <param name="Unavailable">
/// Banka o an cevap veremedi ve transfer HİÇ KABUL EDİLMEDİ. Kalıcı başarısızlıktan
/// farkı ortada satır olmaması: çağıranın yeniden denemesi bekleniyor.
/// </param>
public sealed record AcceptResult(
    string? BankReference, string? Status, bool Replayed, bool Unavailable)
{
    public static AcceptResult Ok(string bankReference, string status, bool replayed) =>
        new(bankReference, status, replayed, Unavailable: false);

    public static AcceptResult TemporarilyUnavailable() =>
        new(BankReference: null, Status: null, Replayed: false, Unavailable: true);
}

using HiWallet.Bank.Fake.Infrastructure.Storage;
using Microsoft.Extensions.Options;

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
    BankFakeStore store,
    IOptions<BankFakeOptions> options,
    TimeProvider timeProvider,
    ILogger<AcceptTransferHandler> logger)
{
    private readonly BankFakeOptions _options = options.Value;

    public AcceptResult Handle(AcceptTransferCommand command)
    {
        var now = timeProvider.GetUtcNow();

        BankTransfer transfer;

        // Bütün adım TEK kilit altında: iki request aynı anahtarla aynı anda gelirse
        // ikincisi birincinin kaydını görmeli. "Önce bak sonra ekle" ancak arada
        // kimse araya giremiyorsa doğru.
        lock (store.Gate)
        {
            // Bankanın KENDİ idempotency koruması. Bizim tarafımızdaki dedup'tan
            // bağımsız: gerçek entegrasyonda karşı tarafın koruma yaptığına
            // güvenilmez, iki taraf da kendi kaydını tutar.
            if (store.TransfersByKey.TryGetValue(command.IdempotencyKey, out var existing))
            {
                return Accepted(existing, replayed: true, now);
            }

            var scenario = store.Scenarios.GetValueOrDefault(command.ClientReference)
                           ?? MaterializeDefault(command.ClientReference);

            var outcome = ResolveOutcome(scenario);

            if (scenario is not null)
            {
                scenario.Attempts++;
            }

            if (outcome is TransferOutcome.TransientFailure)
            {
                // Sayaç azaltılıyor: bir sonraki deneme bir eksiğini görsün.
                scenario!.RemainingTransientFailures--;

                logger.LogInformation(
                    "Banka şu an cevap veremiyor. Client ref {ClientReference}, kalan {Remaining}",
                    command.ClientReference, scenario.RemainingTransientFailures);

                return AcceptResult.TemporarilyUnavailable();
            }

            var delay = outcome is TransferOutcome.DelayedSuccess && scenario is { DelayMilliseconds: > 0 }
                ? TimeSpan.FromMilliseconds(scenario.DelayMilliseconds)
                : _options.SettlementDelay;

            transfer = new BankTransfer
            {
                BankReference = NewBankReference(),
                ClientReference = command.ClientReference,
                IdempotencyKey = command.IdempotencyKey,
                Amount = command.Amount,
                Currency = command.Currency,
                DestinationIban = command.DestinationIban,
                Outcome = outcome,
                // Sonuç anında hazır olsa bile bir gecikme var: asenkron yapının tek
                // gözlemlenebilir tarafı bu pencere.
                ResolveAt = now + delay,
                Fee = outcome is TransferOutcome.Failure ? 0m : _options.TransferFee,
                AcceptedAt = now
            };

            store.Add(transfer);
        }

        logger.LogInformation(
            "Transfer kabul edildi. {BankReference} ← client ref {ClientReference}, sonuç {Outcome} {ResolveAt}",
            transfer.BankReference, transfer.ClientReference, transfer.Outcome, transfer.ResolveAt);

        return Accepted(transfer, replayed: false, now);
    }

    private static AcceptResult Accepted(BankTransfer transfer, bool replayed, DateTimeOffset now) =>
        AcceptResult.Ok(transfer.BankReference, TransferResolution.StatusOf(transfer, now), replayed);

    /// <summary>
    /// Bankanın kendi referansı. Bizim ürettiğimiz hiçbir kimlikten TÜRETİLMİYOR —
    /// gerçek bankada da öyle: referansı onlar üretiyor ve biz onu saklıyoruz.
    /// Türetseydik, testte "referansı nereden aldık" sorusu cevapsız kalırdı.
    /// </summary>
    private static string NewBankReference() => $"BNK{Guid.NewGuid():N}"[..19].ToUpperInvariant();

    /// <summary>
    /// Senaryosu olmayan çekim için varsayılan davranışı senaryoya dönüştürür.
    /// Varsayılan başarıysa senaryo AÇILMIYOR.
    ///
    /// Senaryo olarak saklanması şart: geçici hata sayacı ve deneme sayısı
    /// denemeler arasında hatırlanmazsa "üç denemede başarılı" davranışı her
    /// request'te başa dönerdi. Yalnızca <see cref="BankFakeStore.Gate"/> altında çağrılır.
    /// </summary>
    private TransferScenario? MaterializeDefault(string clientReference)
    {
        if (_options.DefaultOutcome is TransferOutcome.Success) return null;

        var scenario = new TransferScenario
        {
            ClientReference = clientReference,
            Outcome = _options.DefaultOutcome,
            RemainingTransientFailures =
                _options.DefaultOutcome is TransferOutcome.TransientFailure
                    ? _options.DefaultTransientFailures
                    : 0,
            DelayMilliseconds = 0
        };

        store.Scenarios.Add(clientReference, scenario);

        return scenario;
    }

    private static TransferOutcome ResolveOutcome(TransferScenario? scenario)
    {
        // Senaryo kurulmamışsa başarı. Varsayılanın "başarı" olması testlerin
        // yalnızca ilgilendikleri sapmayı kurmasını sağlıyor.
        if (scenario is null) return TransferOutcome.Success;

        var configured = scenario.Outcome;

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
/// farkı ortada kayıt olmaması: çağıranın yeniden denemesi bekleniyor.
/// </param>
public sealed record AcceptResult(
    string? BankReference, string? Status, bool Replayed, bool Unavailable)
{
    public static AcceptResult Ok(string bankReference, string status, bool replayed) =>
        new(bankReference, status, replayed, Unavailable: false);

    public static AcceptResult TemporarilyUnavailable() =>
        new(BankReference: null, Status: null, Replayed: false, Unavailable: true);
}

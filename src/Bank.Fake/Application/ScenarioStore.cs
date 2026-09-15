using HiWallet.Bank.Fake.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Senaryoların kurulduğu ve okunduğu yer. Test bir çekim için "banka reddedecek"
/// dediğinde bu tabloya yazılıyor.
///
/// Gerçek bir bankada bu uç YOKTUR. Sahte servisin tek özel yeteneği bu: dış dünya
/// kötülüklerini bilinçli tetiklemek. Telafi yolunun çalıştığını kanıtlamanın başka
/// yolu yok.
/// </summary>
public sealed class ScenarioStore(
    IDbContextFactory<BankFakeDbContext> contextFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// Senaryoyu YAZAR ya da mevcut olanı DEĞİŞTİRİR. Aynı çekim için ikinci kez
    /// çağrılırsa üzerine yazıyor; test bir senaryoyu kurup sonra fikrini
    /// değiştirebilmeli ve bunun için kaydı silmek zorunda kalmamalı.
    /// </summary>
    public async Task SetAsync(
        string clientReference, TransferOutcome outcome, int transientFailures,
        int delayMilliseconds, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var existing = await db.Scenarios
            .FirstOrDefaultAsync(s => s.ClientReference == clientReference, ct);

        if (existing is not null)
        {
            db.Scenarios.Remove(existing);
        }

        db.Scenarios.Add(new TransferScenario
        {
            ClientReference = clientReference,
            Outcome = outcome.ToText(),
            RemainingTransientFailures =
                outcome is TransferOutcome.TransientFailure ? transientFailures : 0,
            DelayMilliseconds = delayMilliseconds,
            Attempts = 0,
            CreatedAt = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Senaryonun o anki hali. Testler "kaç denemede sonuçlandı" sorusunu buradan
    /// cevaplıyor.
    /// </summary>
    public async Task<ScenarioState?> FindAsync(string clientReference, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var scenario = await db.Scenarios
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ClientReference == clientReference, ct);

        return scenario is null
            ? null
            : new ScenarioState(
                scenario.ClientReference,
                scenario.Outcome,
                scenario.RemainingTransientFailures,
                scenario.Attempts);
    }
}

public sealed record ScenarioState(
    string ClientReference, string Outcome, int RemainingTransientFailures, int Attempts);

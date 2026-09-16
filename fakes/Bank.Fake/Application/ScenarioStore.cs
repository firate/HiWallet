using HiWallet.Bank.Fake.Infrastructure.Storage;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Senaryoların kurulduğu ve okunduğu yer. Test bir çekim için "banka reddedecek"
/// dediğinde buraya yazılıyor.
///
/// Gerçek bir bankada bu uç YOKTUR. Sahte servisin tek özel yeteneği bu: dış dünya
/// kötülüklerini bilinçli tetiklemek. Telafi yolunun çalıştığını kanıtlamanın başka
/// yolu yok.
/// </summary>
public sealed class ScenarioStore(BankFakeStore store)
{
    /// <summary>
    /// Senaryoyu YAZAR ya da mevcut olanı DEĞİŞTİRİR. Aynı çekim için ikinci kez
    /// çağrılırsa üzerine yazıyor; test bir senaryoyu kurup sonra fikrini
    /// değiştirebilmeli ve bunun için kaydı silmek zorunda kalmamalı.
    /// </summary>
    public void Set(
        string clientReference, TransferOutcome outcome, int transientFailures, int delayMilliseconds)
    {
        var scenario = new TransferScenario
        {
            ClientReference = clientReference,
            Outcome = outcome,
            RemainingTransientFailures =
                outcome is TransferOutcome.TransientFailure ? transientFailures : 0,
            DelayMilliseconds = delayMilliseconds
        };

        lock (store.Gate)
        {
            store.Scenarios[clientReference] = scenario;
        }
    }

    /// <summary>
    /// Senaryonun o anki hali. Testler "kaç denemede sonuçlandı" sorusunu buradan
    /// cevaplıyor. Kopya dönüyor: sayaçlar kilit dışında okunmamalı.
    /// </summary>
    public ScenarioState? Find(string clientReference)
    {
        lock (store.Gate)
        {
            return store.Scenarios.TryGetValue(clientReference, out var scenario)
                ? new ScenarioState(
                    scenario.ClientReference,
                    scenario.Outcome,
                    scenario.RemainingTransientFailures,
                    scenario.Attempts)
                : null;
        }
    }
}

public sealed record ScenarioState(
    string ClientReference, TransferOutcome Outcome, int RemainingTransientFailures, int Attempts);

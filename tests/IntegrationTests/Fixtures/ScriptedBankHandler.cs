using System.Net;
using System.Net.Http.Json;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Bankanın yerine sıralı durum kodları döndüren handler. Sahte banka gerçek bir
/// bankanın DAVRANIŞINI taklit ediyor; bu handler ise tek bir çağrının kaç kez
/// denendiğini sayıyor.
///
/// Liste tükendiğinde son durum kodu tekrar ediyor: "üç kere 503, sonra hep 503"
/// ile "bir kere 503, sonra 202" aynı kurulumla yazılabiliyor.
/// </summary>
internal sealed class ScriptedBankHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
{
    private int _calls;

    /// <summary>Bankaya kaç HTTP çağrısı ulaştığı. Yeniden denemenin tek kanıtı.</summary>
    public int Calls => Volatile.Read(ref _calls);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _calls) - 1;
        var status = statuses[Math.Min(index, statuses.Length - 1)];

        var response = new HttpResponseMessage(status);

        if (status is HttpStatusCode.Accepted)
        {
            response.Content = JsonContent.Create(new
            {
                bankReference = $"BNK-{index}",
                status = "pending",
                replayed = false
            });
        }

        return Task.FromResult(response);
    }
}

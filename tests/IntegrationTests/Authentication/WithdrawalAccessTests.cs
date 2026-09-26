using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Authentication;

/// <summary>
/// Orchestrator kimliği kendisi doğruluyor ve çekimi isteyenin kimliğini saga'ya
/// yazıyor. Hesabın kullanıcıları wallet'ta; orchestrator üyeliği bilmiyor, o yüzden
/// kimlik düşme komutuyla wallet'a gidiyor ve üyeliği wallet doğruluyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalAccessTests(OrchestratorFixture fixture) : IAsyncLifetime
{
    private WithdrawalOrchestratorApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WithdrawalOrchestratorApiFactory(fixture);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private Task<HttpResponseMessage> StartAsync(Guid accountId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId = Guid.NewGuid(),
                amount = 100m,
                currency = "TRY",
                destinationIban = "TR330006100519786457841326"
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return _client.SendAsync(request, ct);
    }

    [Fact]
    public async Task TokenYok_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await StartAsync(Guid.NewGuid(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cekim_IsteyeninKimligiSagayaYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = Guid.NewGuid();
        var subject = TestTokens.SubjectOf(accountId);

        _client.As(subject);
        var response = await StartAsync(accountId, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var withdrawalId = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        await using var db = fixture.CreateContext();
        var saga = await db.Sagas.SingleAsync(s => s.Id == withdrawalId, ct);

        saga.InitiatedBySubject.ShouldBe(subject);
        saga.InitiatedBy().Subject.ShouldBe(subject);
    }

    /// <summary>Çekimi yalnızca isteyen görüyor.</summary>
    [Fact]
    public async Task BaskasininCekimi_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var accountId = Guid.NewGuid();

        _client.As(TestTokens.SubjectOf(accountId));
        var started = await StartAsync(accountId, ct);
        var withdrawalId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        (await _client.GetAsync($"/v1/withdrawals/{withdrawalId}", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        _client.As($"test-{Guid.NewGuid():N}");
        (await _client.GetAsync($"/v1/withdrawals/{withdrawalId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WithdrawalOrchestrator.Application.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Backoffice;

/// <summary>
/// Çekim incelemesi. İnceleme eşiğinin üstündeki çekim (TRY için 10.000) cüzdandan
/// düşüldükten sonra bankaya gitmiyor, bir çalışanın kararını bekliyor. Operasyon rolü
/// serbest bırakıyor (banka komutu gidiyor) ya da iptal ediyor (para cüzdana geri
/// veriliyor ve ters kaydın aktörü iptal eden çalışan). Eşiğin altındaki çekim eskisi
/// gibi hemen bankaya gidiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalReviewTests(OrchestratorFixture fixture) : IAsyncLifetime
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

    private static string NewStaff() => $"calisan-{Guid.NewGuid():N}";

    /// <summary>Müşteri çekimi başlatıyor ve wallet düşmeyi onaylıyor.</summary>
    private async Task<Guid> DebitedAsync(decimal amount, CancellationToken ct)
    {
        var accountId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId = Guid.NewGuid(),
                amount,
                currency = "TRY",
                destinationIban = "TR330006100519786457841326"
            }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        using var customer = _factory.CreateClient().As(TestTokens.SubjectOf(accountId));
        var started = await customer.SendAsync(request, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var sagaId = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdvanceSagaHandler>().HandleAsync(
            new WithdrawalDebited { SagaId = sagaId, LedgerTransactionId = Guid.NewGuid(), TotalDebited = amount + 5m }, ct);

        return sagaId;
    }

    private async Task<(WithdrawalSaga Saga, IReadOnlyList<string> Commands)> ReadAsync(Guid sagaId, CancellationToken ct)
    {
        await using var db = fixture.CreateContext();

        var saga = await db.Sagas.SingleAsync(s => s.Id == sagaId, ct);
        var commands = await db.Outbox
            .Where(m => m.SagaId == sagaId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.RoutingKey)
            .ToListAsync(ct);

        return (saga, commands);
    }

    [Fact]
    public async Task EsiginUstu_IncelemedeBekler_BankayaGitmez()
    {
        var ct = TestContext.Current.CancellationToken;

        var sagaId = await DebitedAsync(15_000m, ct);

        var (saga, commands) = await ReadAsync(sagaId, ct);
        saga.State.ShouldBe(WithdrawalState.UnderReview);
        commands.ShouldNotContain(nameof(StartBankTransfer));
    }

    [Fact]
    public async Task EsiginAlti_HemenBankayaGider()
    {
        var ct = TestContext.Current.CancellationToken;

        var sagaId = await DebitedAsync(250m, ct);

        var (saga, commands) = await ReadAsync(sagaId, ct);
        saga.State.ShouldBe(WithdrawalState.BankTransferPending);
        commands.ShouldContain(nameof(StartBankTransfer));
    }

    [Fact]
    public async Task Operasyon_SerbestBirakir_BankaKomutuGider()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(15_000m, ct);
        var staff = NewStaff();

        var response = await _client.AsStaff(staff, StaffRoles.Operations)
            .PostAsync($"/v1/withdrawals/{sagaId}/release", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString()
            .ShouldBe("bank_transfer_pending");

        var (saga, commands) = await ReadAsync(sagaId, ct);
        saga.ReviewedBy.ShouldBe(staff);
        commands.ShouldContain(nameof(StartBankTransfer));
    }

    /// <summary>
    /// İptal parayı geri veriyor: ters kayıt komutu çalışanı aktör olarak taşıyor, wallet
    /// iadeyi yazınca saga banka reddinden ayrı bir durumda bitiyor.
    /// </summary>
    [Fact]
    public async Task Operasyon_IptalEder_IadeCalisaninAdinaYazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(15_000m, ct);
        var staff = NewStaff();

        var response = await _client.AsStaff(staff, StaffRoles.Operations)
            .PostAsJsonAsync($"/v1/withdrawals/{sagaId}/cancel", new { reason = "Müşteri talebi" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString()
            .ShouldBe("cancelling");

        await using (var db = fixture.CreateContext())
        {
            var refund = await db.Outbox.SingleAsync(
                m => m.SagaId == sagaId && m.RoutingKey == nameof(RefundWithdrawal), ct);
            var actor = JsonDocument.Parse(refund.Payload).RootElement.GetProperty("actor");

            actor.GetProperty("type").GetString().ShouldBe("employee");
            actor.GetProperty("id").GetString().ShouldBe(staff);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AdvanceSagaHandler>().HandleAsync(
                new WithdrawalRefunded { SagaId = sagaId, LedgerTransactionId = Guid.NewGuid() }, ct);
        }

        var (saga, _) = await ReadAsync(sagaId, ct);
        saga.State.ShouldBe(WithdrawalState.Cancelled);
        saga.FailureReason.ShouldBe("Müşteri talebi");
    }

    [Fact]
    public async Task IptalSebepsiz_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(15_000m, ct);

        var response = await _client.AsStaff(NewStaff(), StaffRoles.Operations)
            .PostAsJsonAsync($"/v1/withdrawals/{sagaId}/cancel", new { reason = "" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>İncelemede olmayan çekimde karar yok: zaten bankada.</summary>
    [Fact]
    public async Task IncelemedeOlmayanCekim_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(250m, ct);

        var response = await _client.AsStaff(NewStaff(), StaffRoles.Operations)
            .PostAsync($"/v1/withdrawals/{sagaId}/release", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OlmayanCekim_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.AsStaff(NewStaff(), StaffRoles.Operations)
            .PostAsync($"/v1/withdrawals/{Guid.NewGuid()}/release", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DestekVeMusteriKararVeremez_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(15_000m, ct);

        (await _client.AsStaff(NewStaff(), StaffRoles.Support).PostAsync($"/v1/withdrawals/{sagaId}/release", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.As($"test-{Guid.NewGuid():N}").PostAsync($"/v1/withdrawals/{sagaId}/release", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ReadAsync(sagaId, ct)).Saga.State.ShouldBe(WithdrawalState.UnderReview);
    }

    /// <summary>İnceleme kuyruğu: en eski önce; her çalışan görüyor.</summary>
    [Fact]
    public async Task Kuyruk_IncelemedekileriGosterir()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await DebitedAsync(15_000m, ct);

        var queue = await _client.AsStaff(NewStaff(), StaffRoles.Support)
            .GetFromJsonAsync<JsonElement>("/v1/withdrawals?state=under_review&size=100", ct);

        var item = queue.GetProperty("items").EnumerateArray()
            .Single(w => w.GetProperty("withdrawalId").GetGuid() == sagaId);
        item.GetProperty("walletId").GetGuid().ShouldNotBe(Guid.Empty);
        item.GetProperty("accountId").GetGuid().ShouldNotBe(Guid.Empty);
    }
}

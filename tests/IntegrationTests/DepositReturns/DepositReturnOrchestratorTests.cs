using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Actors;
using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WithdrawalOrchestrator.Application.DepositReturns;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.DepositReturns;

/// <summary>
/// Askıdaki havalenin iadesinin orchestrator tarafı. Çalışan iadeyi başlatıyor
/// (<c>deposit.resolve</c>); saga ilk komutu outbox'a koyuyor ve her event'le bir sonrakini.
/// Bankanın sonucu çekimdekiyle aynı event'lerle geliyor; saga kimliğinden iadeye gidiyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DepositReturnOrchestratorTests(OrchestratorFixture fixture) : IAsyncLifetime
{
    private WithdrawalOrchestratorApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WithdrawalOrchestratorApiFactory(fixture);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Staff(string subject, params string[] permissions) =>
        _factory.CreateClient().AsStaff(subject, permissions);

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, Guid deposit, string? key, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/deposit-returns")
        {
            Content = JsonContent.Create(new { suspendedDepositId = deposit })
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request, ct);
    }

    private async Task<Guid> StartedAsync(CancellationToken ct)
    {
        using var staff = Staff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositResolve);
        var response = await StartAsync(staff, Guid.NewGuid(), Guid.NewGuid().ToString(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("depositReturnId").GetGuid();
    }

    private async Task<(DepositReturnSaga Saga, IReadOnlyList<(string Key, string Payload)> Commands)> ReadAsync(
        Guid sagaId, CancellationToken ct)
    {
        await using var db = fixture.CreateContext();

        var saga = await db.DepositReturns.AsNoTracking().SingleAsync(s => s.Id == sagaId, ct);
        var commands = await db.Outbox
            .Where(m => m.SagaId == sagaId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.RoutingKey, m.Payload })
            .ToListAsync(ct);

        return (saga, [.. commands.Select(c => (c.RoutingKey, c.Payload))]);
    }

    private async Task ApplyAsync(Func<IServiceProvider, Task> apply)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await apply(scope.ServiceProvider);
    }

    private Task DebitedAsync(Guid sagaId, CancellationToken ct) => ApplyAsync(services =>
        services.GetRequiredService<AdvanceDepositReturnHandler>().HandleAsync(new SuspenseDebitedForReturn
        {
            SagaId = sagaId,
            LedgerTransactionId = Guid.NewGuid(),
            Amount = 75m,
            Currency = "TRY",
            Provider = "bank-fake",
            DepositBankReference = "GLN0000000000000001"
        }, ct));

    private Task BankResultAsync<T>(T @event) where T : notnull => ApplyAsync(services =>
        services.GetRequiredService<BankTransferResults>().HandleAsync(@event, TestContext.Current.CancellationToken));

    /// <summary>
    /// Saga açılıyor ve askıdan düşme komutu aynı commit'te outbox'ta; aktörü isteyen çalışan.
    /// Tutar bilinmiyor: çalışandan yalnızca havalenin kimliği geliyor.
    /// </summary>
    [Fact]
    public async Task Calisan_IadeBaslatir_AskidanDusmeKomutuOutboxta()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = $"calisan-{Guid.NewGuid():N}";
        var deposit = Guid.NewGuid();
        using var staff = Staff(subject, StaffPermissions.DepositView, StaffPermissions.DepositResolve);

        var response = await StartAsync(staff, deposit, Guid.NewGuid().ToString(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("state").GetString().ShouldBe("initiated");
        body.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        var sagaId = body.GetProperty("depositReturnId").GetGuid();

        var (saga, commands) = await ReadAsync(sagaId, ct);
        saga.SuspendedDepositId.ShouldBe(deposit);
        saga.RequestedBy.ShouldBe(subject);
        var (key, payload) = commands.ShouldHaveSingleItem();
        key.ShouldBe(nameof(DebitSuspenseForReturn));
        var command = JsonSerializer.Deserialize<DebitSuspenseForReturn>(payload, JsonSerializerOptions.Web)!;
        command.SuspendedDepositId.ShouldBe(deposit);
        command.Actor.ShouldBe(new CommandActor { Type = ActorTypes.Employee, Id = subject });

        var status = await staff.GetFromJsonAsync<JsonElement>($"/v1/deposit-returns/{sagaId}", ct);
        status.GetProperty("suspendedDepositId").GetGuid().ShouldBe(deposit);
        status.GetProperty("state").GetString().ShouldBe("initiated");
    }

    [Fact]
    public async Task AyniAnahtar_AyniIade_IkinciKomutYok()
    {
        var ct = TestContext.Current.CancellationToken;
        var deposit = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        using var staff = Staff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositResolve);

        var first = await (await StartAsync(staff, deposit, key, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var second = await (await StartAsync(staff, deposit, key, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        second.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        second.GetProperty("depositReturnId").GetGuid().ShouldBe(first.GetProperty("depositReturnId").GetGuid());
        (await ReadAsync(first.GetProperty("depositReturnId").GetGuid(), ct)).Commands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnahtarsizIstek400_GormekBaslatmayaYetmez403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var resolver = Staff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositResolve);
        using var viewer = Staff($"calisan-{Guid.NewGuid():N}", StaffPermissions.DepositView);
        using var customer = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");

        (await StartAsync(resolver, Guid.NewGuid(), key: null, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StartAsync(viewer, Guid.NewGuid(), Guid.NewGuid().ToString(), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StartAsync(customer, Guid.NewGuid(), Guid.NewGuid().ToString(), ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"/v1/deposit-returns/{Guid.NewGuid()}", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Mutlu yol: düşüldü → bankaya komut (IBAN yok, havalenin referansı var) → banka gönderdi
    /// → kapanış komutu → tamamlandı. Bankanın sonucu çekimin event'iyle geliyor.
    /// </summary>
    [Fact]
    public async Task MutluYol_BankayaGider_Kapanir_Tamamlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await StartedAsync(ct);

        await DebitedAsync(sagaId, ct);

        var (pending, commands) = await ReadAsync(sagaId, ct);
        pending.State.ShouldBe(DepositReturnState.BankTransferPending);
        var bank = JsonSerializer.Deserialize<ReturnBankDeposit>(commands[^1].Payload, JsonSerializerOptions.Web)!;
        commands[^1].Key.ShouldBe(nameof(ReturnBankDeposit));
        bank.CommandId.ShouldBe(pending.BankCommandId!.Value);
        bank.Provider.ShouldBe("bank-fake");
        bank.DepositBankReference.ShouldBe("GLN0000000000000001");
        bank.Amount.ShouldBe(75m);

        await BankResultAsync(new BankTransferSucceeded { SagaId = sagaId, BankReference = "IADE-1", FeeAmount = 1.5m });

        var (settling, afterBank) = await ReadAsync(sagaId, ct);
        settling.State.ShouldBe(DepositReturnState.Settling);
        afterBank[^1].Key.ShouldBe(nameof(SettleDepositReturn));
        JsonSerializer.Deserialize<SettleDepositReturn>(afterBank[^1].Payload, JsonSerializerOptions.Web)!
            .FeeAmount.ShouldBe(1.5m);

        await ApplyAsync(services => services.GetRequiredService<AdvanceDepositReturnHandler>()
            .HandleAsync(new DepositReturnSettled { SagaId = sagaId, LedgerTransactionId = Guid.NewGuid() }, ct));

        (await ReadAsync(sagaId, ct)).Saga.State.ShouldBe(DepositReturnState.Completed);
    }

    /// <summary>Banka reddetti: askıya geri koyma komutu, sonra iade başarısız; havale karara açık.</summary>
    [Fact]
    public async Task BankaReddi_AskiyaGeriKonur_Basarisiz()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await StartedAsync(ct);
        await DebitedAsync(sagaId, ct);

        await BankResultAsync(new BankTransferFailed { SagaId = sagaId, Reason = "Hesap kapalı" });

        var (restoring, commands) = await ReadAsync(sagaId, ct);
        restoring.State.ShouldBe(DepositReturnState.Restoring);
        commands[^1].Key.ShouldBe(nameof(RestoreSuspendedDeposit));

        await ApplyAsync(services => services.GetRequiredService<AdvanceDepositReturnHandler>()
            .HandleAsync(new SuspendedDepositRestored { SagaId = sagaId, LedgerTransactionId = Guid.NewGuid() }, ct));

        var failed = (await ReadAsync(sagaId, ct)).Saga;
        failed.State.ShouldBe(DepositReturnState.Failed);
        failed.FailureRule.ShouldBe(DepositReturnFailureRules.BankRejected);
    }

    /// <summary>Wallet düşmedi (aktarılmış havale): saga burada bitiyor, bankaya hiçbir şey gitmiyor.</summary>
    [Fact]
    public async Task WalletReddi_SagaBiter_BankayaGitmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await StartedAsync(ct);

        await ApplyAsync(services => services.GetRequiredService<AdvanceDepositReturnHandler>()
            .HandleAsync(new SuspenseDebitForReturnRejected
            {
                SagaId = sagaId,
                Reason = "Bu havale için karar verilmiş.",
                Rule = "deposit_already_resolved"
            }, ct));

        var (saga, commands) = await ReadAsync(sagaId, ct);
        saga.State.ShouldBe(DepositReturnState.Rejected);
        saga.FailureRule.ShouldBe("deposit_already_resolved");
        commands.ShouldHaveSingleItem().Key.ShouldBe(nameof(DebitSuspenseForReturn));
    }
}

using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Actors;
using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.Shared.Contracts.Deposits;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.DepositReturns;
using HiWallet.WalletService.Application.Deposits;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.DepositReturns;

/// <summary>
/// Askıdaki havalenin iadesinin wallet tarafı: askıdan düşme (askı −, clearing +, aktör
/// iadeyi isteyen çalışan), banka gönderince muhasebenin kapanması (clearing −, nostro +)
/// ve banka reddedince askıya geri koyma. Askıdan düşülen havale aktarılamıyor; geri konan
/// havale yeniden karara açılıyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DepositReturnWalletTests(PostgresFixture postgres)
{
    private const string Employee = "calisan-iade";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DebitSuspenseForReturnHandler Debit() =>
        new(postgres.ContextFactory, new SystemClock(), NullLogger<DebitSuspenseForReturnHandler>.Instance);

    private SettleDepositReturnHandler Settle() =>
        new(postgres.ContextFactory, TestProviders.Policy, new SystemClock(), NullLogger<SettleDepositReturnHandler>.Instance);

    private RestoreSuspendedDepositHandler Restore() =>
        new(postgres.ContextFactory, new SystemClock(), NullLogger<RestoreSuspendedDepositHandler>.Instance);

    private async Task<(Guid Id, string BankReference)> SuspendedAsync(decimal amount, CancellationToken ct)
    {
        var bankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant();
        var result = await new ProcessDepositHandler(
                postgres.ContextFactory, new FakeHolderIdentity(), TestKycLimits.Policy,
                new SystemClock(), NullLogger<ProcessDepositHandler>.Instance)
            .HandleAsync(new BankDepositReceived
            {
                Provider = SystemAccounts.BankFake,
                BankReference = bankReference,
                Amount = amount,
                Currency = "TRY",
                Description = "kira",
                SenderNationalId = "10000000078",
                ReceivedAt = DateTimeOffset.UtcNow
            }, ct);

        result.HeldFor.ShouldNotBeNull();
        return (result.LedgerTransactionId, bankReference);
    }

    private static DebitSuspenseForReturn DebitCommand(Guid deposit, Guid sagaId) => new()
    {
        CommandId = Guid.NewGuid(),
        SagaId = sagaId,
        SuspendedDepositId = deposit,
        Actor = new CommandActor { Type = ActorTypes.Employee, Id = Employee }
    };

    private static T Read<T>(WithdrawalReply reply) =>
        JsonSerializer.Deserialize<T>(reply.Payload, Json) ?? throw new InvalidOperationException("Boş cevap.");

    private async Task<decimal> BalanceAsync(Guid ledgerAccountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerBalances.Where(b => b.LedgerAccountId == ledgerAccountId).SumAsync(b => b.Balance, ct);
    }

    private async Task<LedgerTransaction> TransactionAsync(Guid id, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        return await db.LedgerTransactions.AsNoTracking().Include(t => t.Entries).SingleAsync(t => t.Id == id, ct);
    }

    private async Task<string?> StatusAsync(Guid deposit, CancellationToken ct)
    {
        var page = await new ListSuspendedDepositsHandler(postgres.ContextFactory)
            .HandleAsync(new ListSuspendedDepositsQuery(null, SuspendedDepositPage.MaxSize), ct);

        return page.Items.SingleOrDefault(i => i.Id == deposit)?.Status;
    }

    private async Task<MoveSuspendedDepositResult> MoveAsync(Guid deposit, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();
        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct, KycLevel.Unverified);
        await LedgerSeeder.CreateWalletAsync(db, account, "Ana", ct);
        var number = await db.Accounts.Where(a => a.Id == account).Select(a => a.Number).SingleAsync(ct);

        return await new MoveSuspendedDepositHandler(
                postgres.ContextFactory, TestKycLimits.Policy, new SystemClock(), NullLogger<MoveSuspendedDepositHandler>.Instance)
            .HandleAsync(new MoveSuspendedDepositCommand(deposit, number, Employee, Guid.NewGuid().ToString()), ct);
    }

    /// <summary>
    /// Bankaya giden komutun ihtiyacı olan her şey cevapta: tutar, para birimi, banka ve
    /// havalenin referansı. Orchestrator havaleyi bilmiyor.
    /// </summary>
    [Fact]
    public async Task Dusme_AskiEksiClearingArti_AktorCalisan_IadeSuruyor()
    {
        var ct = TestContext.Current.CancellationToken;
        var (deposit, bankReference) = await SuspendedAsync(40m, ct);
        var sagaId = Guid.NewGuid();
        var suspenseBefore = await BalanceAsync(SystemAccounts.SuspenseBankTry, ct);
        var clearingBefore = await BalanceAsync(SystemAccounts.ClearingBankTry, ct);

        var reply = await Debit().HandleAsync(DebitCommand(deposit, sagaId), ct);

        reply.RoutingKey.ShouldBe(nameof(SuspenseDebitedForReturn));
        var debited = Read<SuspenseDebitedForReturn>(reply);
        debited.SagaId.ShouldBe(sagaId);
        debited.Amount.ShouldBe(40m);
        debited.Currency.ShouldBe("TRY");
        debited.Provider.ShouldBe(SystemAccounts.BankFake);
        debited.DepositBankReference.ShouldBe(bankReference);

        var tx = await TransactionAsync(debited.LedgerTransactionId, ct);
        tx.Type.ShouldBe(LedgerTransactionType.DepositReturn);
        tx.ActorType.ShouldBe(ActorType.Employee);
        tx.ActorId.ShouldBe(Employee);
        tx.CorrelationId.ShouldBe(sagaId);
        (await BalanceAsync(SystemAccounts.SuspenseBankTry, ct) - suspenseBefore).ShouldBe(-40m);
        (await BalanceAsync(SystemAccounts.ClearingBankTry, ct) - clearingBefore).ShouldBe(40m);

        (await StatusAsync(deposit, ct)).ShouldBe("returning");
        (await Should.ThrowAsync<DepositResolutionRejectedException>(() => MoveAsync(deposit, ct)))
            .Rule.ShouldBe(DepositResolutionRejectedException.ReturnInProgress);
    }

    [Fact]
    public async Task AyniKomutTekrar_LedgereDokunmaz_AyniCevap()
    {
        var ct = TestContext.Current.CancellationToken;
        var (deposit, _) = await SuspendedAsync(15m, ct);
        var command = DebitCommand(deposit, Guid.NewGuid());

        var first = await Debit().HandleAsync(command, ct);
        var suspense = await BalanceAsync(SystemAccounts.SuspenseBankTry, ct);
        var second = await Debit().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        Read<SuspenseDebitedForReturn>(second).LedgerTransactionId
            .ShouldBe(Read<SuspenseDebitedForReturn>(first).LedgerTransactionId);
        (await BalanceAsync(SystemAccounts.SuspenseBankTry, ct)).ShouldBe(suspense);
    }

    /// <summary>
    /// Aktarılmış ya da iadesi süren havale, ya da olmayan havale: ret cevabı, ledger'a
    /// hiçbir şey yazılmıyor. Saga orada bitiyor.
    /// </summary>
    [Fact]
    public async Task AktarilmisIadesiSurenVeOlmayanHavale_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (moved, _) = await SuspendedAsync(10m, ct);
        await MoveAsync(moved, ct);
        var (returning, _) = await SuspendedAsync(10m, ct);
        await Debit().HandleAsync(DebitCommand(returning, Guid.NewGuid()), ct);
        var suspense = await BalanceAsync(SystemAccounts.SuspenseBankTry, ct);

        var cases = new[]
        {
            (moved, DepositResolutionRejectedException.AlreadyResolved),
            (returning, DepositResolutionRejectedException.ReturnInProgress),
            (Guid.NewGuid(), DepositResolutionRejectedException.NotFound)
        };

        foreach (var (deposit, rule) in cases)
        {
            var reply = await Debit().HandleAsync(DebitCommand(deposit, Guid.NewGuid()), ct);

            reply.RoutingKey.ShouldBe(nameof(SuspenseDebitForReturnRejected));
            Read<SuspenseDebitForReturnRejected>(reply).Rule.ShouldBe(rule);
        }

        (await BalanceAsync(SystemAccounts.SuspenseBankTry, ct)).ShouldBe(suspense);
    }

    /// <summary>
    /// Banka gönderdi: clearing kapanıyor, para nostro'dan çıkıyor. bank-fake'in ücreti
    /// faturayla ödeniyor; burada tahakkuk ediyor. Havale listeden çıkıyor.
    /// </summary>
    [Fact]
    public async Task Kapanis_ClearingKapanir_NostrodanCikar_ListedenCikar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (deposit, _) = await SuspendedAsync(60m, ct);
        var sagaId = Guid.NewGuid();
        await Debit().HandleAsync(DebitCommand(deposit, sagaId), ct);
        var clearingBefore = await BalanceAsync(SystemAccounts.ClearingBankTry, ct);
        var nostroBefore = await BalanceAsync(SystemAccounts.NostroBankTry, ct);

        var reply = await Settle().HandleAsync(new SettleDepositReturn
        {
            CommandId = Guid.NewGuid(),
            SagaId = sagaId,
            FeeAmount = 1.5m,
            BankReference = "IADE-TEST"
        }, ct);

        reply.RoutingKey.ShouldBe(nameof(DepositReturnSettled));
        var settlement = await TransactionAsync(Read<DepositReturnSettled>(reply).LedgerTransactionId, ct);
        settlement.Type.ShouldBe(LedgerTransactionType.Settlement);
        settlement.CorrelationId.ShouldBe(sagaId);
        (await BalanceAsync(SystemAccounts.ClearingBankTry, ct) - clearingBefore).ShouldBe(-60m);
        (await BalanceAsync(SystemAccounts.NostroBankTry, ct) - nostroBefore).ShouldBe(60m);

        await using var db = postgres.CreateContext();
        (await db.ProviderFees.AsNoTracking().SingleAsync(f => f.TransactionId == settlement.Id, ct))
            .ExpectedAmount.ShouldBe(1.5m);

        (await StatusAsync(deposit, ct)).ShouldBeNull();
    }

    /// <summary>
    /// Banka reddetti: düşmenin aynası yazılıyor, aktör iade saga'sı. Havale yeniden askıda
    /// ve karara açık: aktarılabiliyor ya da yeniden iade edilebiliyor.
    /// </summary>
    [Fact]
    public async Task GeriKoyma_AskiyaDoner_HavaleYenidenKarardaAcik()
    {
        var ct = TestContext.Current.CancellationToken;
        var (deposit, _) = await SuspendedAsync(25m, ct);
        var sagaId = Guid.NewGuid();
        await Debit().HandleAsync(DebitCommand(deposit, sagaId), ct);
        var suspenseBefore = await BalanceAsync(SystemAccounts.SuspenseBankTry, ct);

        var reply = await Restore().HandleAsync(new RestoreSuspendedDeposit { CommandId = Guid.NewGuid(), SagaId = sagaId }, ct);

        reply.RoutingKey.ShouldBe(nameof(SuspendedDepositRestored));
        var restore = await TransactionAsync(Read<SuspendedDepositRestored>(reply).LedgerTransactionId, ct);
        restore.Type.ShouldBe(LedgerTransactionType.Refund);
        restore.ActorType.ShouldBe(ActorType.System);
        restore.ActorId.ShouldBe(SystemFlows.DepositReturn);
        (await BalanceAsync(SystemAccounts.SuspenseBankTry, ct) - suspenseBefore).ShouldBe(25m);
        (await StatusAsync(deposit, ct)).ShouldBe("open");

        var again = await Debit().HandleAsync(DebitCommand(deposit, Guid.NewGuid()), ct);
        again.RoutingKey.ShouldBe(nameof(SuspenseDebitedForReturn));
        await Restore().HandleAsync(new RestoreSuspendedDeposit
        {
            CommandId = Guid.NewGuid(),
            SagaId = Read<SuspenseDebitedForReturn>(again).SagaId
        }, ct);

        (await MoveAsync(deposit, ct)).Replayed.ShouldBeFalse();
        (await StatusAsync(deposit, ct)).ShouldBeNull();
    }
}

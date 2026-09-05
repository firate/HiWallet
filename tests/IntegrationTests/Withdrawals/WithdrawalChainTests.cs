using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.BankService.Application;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Messaging;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// <b>Zincirin tamamı.</b> Üç deployable ayrı ayrı ayağa kalkıyor ve aralarında
/// yalnızca broker var — üretimdeki gibi:
///
/// <code>
///   POST /v1/withdrawals → orchestrator → outbox → relay
///        → wallet-consumer  (ledger'a düşme)  → WithdrawalDebited
///        → bank-service     (transfer)        → BankTransferSucceeded / Failed
///        → orchestrator     (saga ilerler)    → [Completed] ya da iade komutu
/// </code>
///
/// Buraya kadar her parça kendi testleriyle doğrulandı; burada doğrulanan şey
/// PARÇALARIN BİRBİRİNE DEĞDİĞİ YER: sözleşmeler uyuşuyor mu, routing key'ler
/// tutuyor mu, saga gerçekten ilerliyor mu.
///
/// <b>Broker yoksa atlanıyor.</b> Atlanan test yeşil görünmüyor; hangi güvencenin
/// ölçülmediği çıktıdan okunabiliyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalChainTests(
    PostgresFixture postgres, OrchestratorFixture orchestratorDb, BankFixture bankDb) : IAsyncLifetime
{
    private const string SkipReason =
        "RabbitMQ'ya bağlanılamıyor (RabbitMq__Host ve kimlik bilgileri). Zincir doğrulanmadı.";

    private const string Iban = "TR33 0006 1005 1978 6457 8413 26";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private WithdrawalOrchestratorApiFactory? _orchestrator;
    private WalletConsumerFactory? _walletConsumer;
    private BankServiceFactory? _bank;
    private HttpClient? _api;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();

        if (_orchestrator is not null) await _orchestrator.DisposeAsync();
        if (_walletConsumer is not null) await _walletConsumer.DisposeAsync();
        if (_bank is not null) await _bank.DisposeAsync();

        await DeleteTopologyAsync();
    }

    /// <summary>
    /// Mutlu yol. Çekim baştan sona akıyor ve ledger'da üç bacaklı TEK bir işlem
    /// kalıyor — iade yok.
    /// </summary>
    [Fact]
    public async Task MutluYol_CekimTamamlanir_CuzdandanKomisyonlaDuser()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        await StartAsync(ct);

        var wallet = await NewFundedWalletAsync(1_000m, ct);
        var revenueBefore = await BalanceAsync(SystemAccounts.RevenueTry, ct);

        var withdrawalId = await RequestWithdrawalAsync(wallet, 100m, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Completed, ct);

        await using var db = postgres.CreateContext();

        var tx = await db.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.CorrelationId == withdrawalId
                              && t.Type == LedgerTransactionType.Withdrawal, ct);

        tx.Entries.Count.ShouldBe(3);
        tx.Entries.Sum(e => e.Amount).ShouldBe(0m);
        tx.Entries.Single(e => e.LedgerAccountId == wallet.WalletId).Amount.ShouldBe(-102m);
        tx.Entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingBankTry).Amount.ShouldBe(100m);

        // İade YOK: mutlu yolda ters kayıt yazılmamalı.
        (await db.LedgerTransactions.AnyAsync(
            t => t.CorrelationId == withdrawalId && t.Type == LedgerTransactionType.Refund, ct))
            .ShouldBeFalse();

        (await BalanceAsync(wallet.WalletId, ct)).ShouldBe(898m);
        (await BalanceAsync(SystemAccounts.RevenueTry, ct)).ShouldBe(revenueBefore + 2m);

        // Saga bankanın referansını da almış olmalı.
        var saga = await SagaAsync(withdrawalId, ct);
        saga.TotalDebited.ShouldBe(102m);
        saga.BankCommandId.ShouldNotBeNull();
    }

    /// <summary>
    /// <b>Telafi yolu — bu testin varlık sebebi.</b> Banka reddediyor ve para
    /// müşteriye ÜÇ BACAĞIN TAMAMIYLA geri dönüyor: komisyon dahil.
    ///
    /// `revenue` bacağı atlansaydı ledger yine dengeli olurdu ve hiçbir invariant
    /// bağırmazdı — yakalayan tek şey bu tür bir uçtan uca kontrol.
    /// </summary>
    [Fact]
    public async Task BankaReddederse_ParaKomisyonlaBirlikteGeriDoner()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        // Banka REDDEDİCİ olarak başlatılıyor. Senaryoyu API'den kurmak da mümkündü
        // ama saga kimliği ancak istek kabul edildikten sonra biliniyor ve banka
        // komutu teorik olarak araya girebilirdi. Varsayılanı servis başlamadan
        // vermek o pencereyi tamamen kapatıyor.
        await StartAsync(ct, TransferOutcome.PermanentFailure);

        var wallet = await NewFundedWalletAsync(1_000m, ct);
        var revenueBefore = await BalanceAsync(SystemAccounts.RevenueTry, ct);
        var clearingBefore = await BalanceAsync(SystemAccounts.ClearingBankTry, ct);

        var withdrawalId = await RequestWithdrawalAsync(wallet, 100m, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Failed, ct);

        await using var db = postgres.CreateContext();

        var refund = await db.LedgerTransactions
            .Include(t => t.Entries)
            .SingleAsync(t => t.CorrelationId == withdrawalId
                              && t.Type == LedgerTransactionType.Refund, ct);

        refund.Entries.Count.ShouldBe(3);
        refund.Entries.Sum(e => e.Amount).ShouldBe(0m);
        refund.Entries.Single(e => e.LedgerAccountId == wallet.WalletId).Amount.ShouldBe(102m);
        refund.Entries.Single(e => e.LedgerAccountId == SystemAccounts.RevenueTry).Amount.ShouldBe(-2m);

        // Üç hesap da başladığı yere döndü.
        (await BalanceAsync(wallet.WalletId, ct)).ShouldBe(1_000m);
        (await BalanceAsync(SystemAccounts.RevenueTry, ct)).ShouldBe(revenueBefore);
        (await BalanceAsync(SystemAccounts.ClearingBankTry, ct)).ShouldBe(clearingBefore);

        // Orijinal düşme SİLİNMEDİ; ledger append-only.
        (await db.LedgerTransactions.CountAsync(t => t.CorrelationId == withdrawalId, ct)).ShouldBe(2);
    }

    /// <summary>
    /// Geçici banka hatası telafi BAŞLATMAMALI. Saga bekliyor, bank-service kendi
    /// içinde yeniden deniyor ve sonunda başarıyor.
    /// </summary>
    [Fact]
    public async Task GeciciBankaHatasi_TelafiBaslatmaz_SonundaTamamlanir()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        // İki geçici hata sonra başarı; gerekçe telafi testindekiyle aynı.
        await StartAsync(ct, TransferOutcome.TransientFailure);

        var wallet = await NewFundedWalletAsync(1_000m, ct);
        var withdrawalId = await RequestWithdrawalAsync(wallet, 100m, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Completed, ct);

        await using var db = postgres.CreateContext();

        // Hiç iade yazılmamış olmalı: geçici hata telafi sebebi değil.
        (await db.LedgerTransactions.AnyAsync(
            t => t.CorrelationId == withdrawalId && t.Type == LedgerTransactionType.Refund, ct))
            .ShouldBeFalse();

        (await BalanceAsync(wallet.WalletId, ct)).ShouldBe(898m);
    }

    /// <summary>
    /// Yetersiz bakiye zincirin ilk adımında kesiliyor: bankaya hiç komut gitmiyor,
    /// ledger'a hiçbir şey yazılmıyor, saga terminal duruma geçiyor.
    /// </summary>
    [Fact]
    public async Task YetersizBakiye_SagaReddedilir_BankayaKomutGitmez()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.SkipUnless(await BrokerSettings.IsReachableAsync(ct), SkipReason);

        await StartAsync(ct);

        var wallet = await NewFundedWalletAsync(50m, ct);
        var withdrawalId = await RequestWithdrawalAsync(wallet, 100m, ct);

        await WaitForStateAsync(withdrawalId, WithdrawalState.Rejected, ct);

        await using var db = postgres.CreateContext();

        (await db.LedgerTransactions.AnyAsync(t => t.CorrelationId == withdrawalId, ct))
            .ShouldBeFalse();

        (await BalanceAsync(wallet.WalletId, ct)).ShouldBe(50m);

        // Banka bu saga'yı hiç görmemiş olmalı.
        await using var bank = bankDb.CreateContext();
        (await bank.Transfers.AnyAsync(t => t.SagaId == withdrawalId, ct)).ShouldBeFalse();

        var saga = await SagaAsync(withdrawalId, ct);
        saga.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    // --- kurulum ve yardımcılar ------------------------------------------------

    private static WithdrawalTopology Topology { get; } =
        new(Options.Create(BrokerSettings.BuildOptions("hiwallet-tests")));

    /// <summary>
    /// Üç host'u ayağa kaldırır. Topolojiyi TEST kuruyor: uygulamalar da kuruyor ama
    /// arka planda, ve ilk komut relay bağlanmadan önce yazılabiliyor.
    /// </summary>
    private async Task StartAsync(CancellationToken ct, TransferOutcome? bankOutcome = null)
    {
        await using (var connection = new RabbitMqConnection(
                         Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-topology"))))
        {
            await using var channel = await (await connection.GetAsync(ct))
                .CreateChannelAsync(cancellationToken: ct);

            await Topology.DeclareAsync(channel, ct);
        }

        _orchestrator ??= new WithdrawalOrchestratorApiFactory(orchestratorDb, useRealBroker: true);
        _walletConsumer ??= new WalletConsumerFactory(postgres);
        _bank ??= new BankServiceFactory(bankDb, bankOutcome);

        _api ??= _orchestrator.CreateClient();
        _bank.CreateClient().Dispose();

        // CreateClient host'u kuruyor; tüketiciler böylece dinlemeye başlıyor.
        _walletConsumer.CreateClient().Dispose();
    }

    private async Task<Guid> RequestWithdrawalAsync(Wallet wallet, decimal amount, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId = wallet.AccountId,
                walletId = wallet.WalletId,
                amount,
                currency = "TRY",
                destinationIban = Iban
            })
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await _api!.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<AcceptedBody>(JsonOptions, ct);

        return body.ShouldNotBeNull().WithdrawalId;
    }

    private async Task<WithdrawalSaga> SagaAsync(Guid sagaId, CancellationToken ct)
    {
        await using var db = orchestratorDb.CreateContext();

        return await db.Sagas.AsNoTracking().SingleAsync(s => s.Id == sagaId, ct);
    }

    private async Task WaitForStateAsync(Guid sagaId, WithdrawalState expected, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + Timeout;
        WithdrawalState? last = null;

        while (DateTime.UtcNow < deadline)
        {
            await using (var db = orchestratorDb.CreateContext())
            {
                var saga = await db.Sagas.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == sagaId, ct);

                last = saga?.State;

                if (last == expected) return;
            }

            await Task.Delay(250, ct);
        }

        throw new TimeoutException(
            $"{Timeout.TotalSeconds} sn içinde saga {expected} durumuna geçmedi. Son durum: {last}.");
    }

    private async Task<Wallet> NewFundedWalletAsync(decimal amount, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, "zincir", ct);

        await LedgerSeeder.FundAsync(db, walletId, amount, ct);

        return new Wallet(accountId, walletId);
    }

    private async Task<decimal> BalanceAsync(Guid ledgerAccountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return (await db.LedgerBalances.AsNoTracking()
            .SingleAsync(b => b.LedgerAccountId == ledgerAccountId, ct)).Balance;
    }

    /// <summary>
    /// Koşunun açtığı exchange ve kuyrukları siler; ön ek koşuya özel olduğu için
    /// bırakılsalardı broker'da her koşuda yeni bir çöp yığını birikirdi.
    /// </summary>
    private static async Task DeleteTopologyAsync()
    {
        if (!BrokerSettings.Configured) return;

        try
        {
            await using var connection = new RabbitMqConnection(
                Options.Create(BrokerSettings.BuildOptions("hiwallet-tests-cleanup")));

            await using var channel = await (await connection.GetAsync(CancellationToken.None))
                .CreateChannelAsync();

            foreach (var queue in new[]
                     {
                         Topology.WalletQueue, Topology.BankQueue,
                         Topology.OrchestratorQueue, Topology.DeadLetterQueue
                     })
            {
                await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            }

            await channel.ExchangeDeleteAsync(Topology.Exchange, ifUnused: false);
            await channel.ExchangeDeleteAsync(Topology.DeadLetterExchange, ifUnused: false);
        }
        catch (Exception)
        {
            // Temizlik testin sonucunu etkilememeli.
        }
    }

    private sealed record Wallet(Guid AccountId, Guid WalletId);

    private sealed record AcceptedBody(Guid WithdrawalId, string State, bool Replayed);
}

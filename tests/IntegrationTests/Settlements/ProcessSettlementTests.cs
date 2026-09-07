using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Settlements;

/// <summary>
/// Settlement'ın ledger etkisi (adım 5.5, <c>ledger-schema.md</c> "Settlement
/// kayıtları"). Top-up'ta açılan alacak kapanıyor, para <c>nostro</c>'ya geçiyor.
///
/// <b>İşaret konvansiyonu.</b> <c>nostro</c> bir varlık hesabı ve bu ledger'da
/// varlıklar NEGATİF duruyor: <c>-97.1</c> "bankada 97.1 var" demek.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProcessSettlementTests(PostgresFixture postgres)
{
    private const string Stripe = "stripe-fake";
    private const string Bank = "bank-fake";

    private ProcessSettlementHandler Handler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessSettlementHandler>.Instance);

    private ProcessTopupHandler TopupHandler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessTopupHandler>.Instance);

    /// <summary>
    /// Net model: üç bacak. Clearing kapanıyor, gider yazılıyor, nostro artıyor.
    /// </summary>
    [Fact]
    public async Task NetModel_UcBacak_ClearingKapanirNostroArtar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (before, _) = await SnapshotAsync(SystemAccounts.ClearingStripeTry, ct);
        var (nostroBefore, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);
        var (expenseBefore, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseStripeTry, ct);

        var result = await Handler().HandleAsync(
            Settlement(Stripe, gross: 100m, fee: 2.90m, net: 97.10m), ct);

        var entries = await EntriesAsync(result.LedgerTransactionId!.Value, ct);

        entries.Count.ShouldBe(3);
        entries.Sum(e => e.Amount).ShouldBe(0m);

        entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingStripeTry)
            .Amount.ShouldBe(100m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.ProviderExpenseStripeTry)
            .Amount.ShouldBe(-2.90m);

        // Bankaya giren para: negatif yönde hareket ediyor.
        entries.Single(e => e.LedgerAccountId == SystemAccounts.NostroBankTry)
            .Amount.ShouldBe(-97.10m);

        // Projeksiyon da aynı yönde.
        var (clearingAfter, _) = await SnapshotAsync(SystemAccounts.ClearingStripeTry, ct);
        var (nostroAfter, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);
        var (expenseAfter, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseStripeTry, ct);

        (clearingAfter - before).ShouldBe(100m);
        (nostroAfter - nostroBefore).ShouldBe(-97.10m);
        (expenseAfter - expenseBefore).ShouldBe(-2.90m);
    }

    /// <summary>
    /// Invoiced modelde <c>provider_expense</c> bacağı YOK (madde 10): ücret dönem
    /// sonu faturasıyla ödeniyor. Bacağı burada da yazmak gideri İKİ KEZ kaydederdi.
    /// </summary>
    [Fact]
    public async Task InvoicedModel_GiderBacagiYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler().HandleAsync(
            Settlement(Bank, gross: 200m, fee: 0m, net: 200m), ct);

        var entries = await EntriesAsync(result.LedgerTransactionId!.Value, ct);

        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Amount).ShouldBe(0m);
        entries.ShouldNotContain(e => e.LedgerAccountId == SystemAccounts.ProviderExpenseStripeTry);
    }

    /// <summary>
    /// <b>Asıl kanıt.</b> Settlement, top-up'ta açılan ücret satırını kapatıyor:
    /// gerçekleşen tutar doluyor ve satır settlement kaydına bağlanıyor. Bu
    /// olmadan fatura eşleştirmesi ve mutabakat (5.6, 5.7) dayanaksız kalır.
    /// </summary>
    [Fact]
    public async Task UcretSatiri_GerceklesenTutarlaKapanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var providerRef = $"pi_{Guid.NewGuid():N}";

        var topup = await TopupHandler().HandleAsync(Topup(wallet, 100m, Stripe, providerRef), ct);

        var settlement = await Handler().HandleAsync(
            Settlement(Stripe, gross: 100m, fee: 2.90m, net: 97.10m, refs: [providerRef]), ct);

        await using var db = postgres.CreateContext();
        var fee = await db.ProviderFees
            .AsNoTracking()
            .SingleAsync(f => f.TransactionId == topup.LedgerTransactionId!.Value, ct);

        fee.ActualAmount.ShouldBe(2.90m);
        fee.LedgerTransactionId.ShouldBe(settlement.LedgerTransactionId!.Value);

        // Beklenen değişmedi: tahmin geçmişi korunuyor, fatura uyuşmazlığı analizi
        // (madde 11) beklenen ile gerçekleşeni karşılaştırıyor.
        fee.ExpectedAmount.ShouldBe(3.20m);
    }

    /// <summary>
    /// Aynı batch iki kez gelirse ledger'a ikinci kez yazılmamalı. Yazılsaydı
    /// clearing iki kez kapanır ve nostro olmayan parayla şişerdi.
    /// </summary>
    [Fact]
    public async Task AyniSettlement_IkinciKezIslenmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var message = Settlement(Stripe, gross: 50m, fee: 1.45m, net: 48.55m);

        var (before, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        var first = await Handler().HandleAsync(message, ct);
        var second = await Handler().HandleAsync(message, ct);

        second.Replayed.ShouldBeTrue();
        second.LedgerTransactionId.ShouldBe(first.LedgerTransactionId);

        var (after, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        (after - before).ShouldBe(-48.55m);
    }

    /// <summary>
    /// Tutarsız batch ledger'a HİÇ yazılmamalı. Üç bacaklı kayıt dengeli çıkmazdı
    /// ve trigger'a kadar gitmesine gerek yok.
    /// </summary>
    [Fact]
    public async Task TutarsizBatch_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<SettlementRejectedException>(
            Handler().HandleAsync(Settlement(Stripe, gross: 100m, fee: 2m, net: 90m), ct));
    }

    /// <summary>
    /// Invoiced sağlayıcı settlement'ta ücret bildiremez: ya sağlayıcı modeli
    /// değiştirdi ya konfigürasyon eskidi. İkisi de sessizce yanlış gider kaydı
    /// üretirdi.
    /// </summary>
    [Fact]
    public async Task InvoicedSaglayici_UcretBildirirse_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<SettlementRejectedException>(
            Handler().HandleAsync(Settlement(Bank, gross: 100m, fee: 1.50m, net: 98.50m), ct));
    }

    private static SettlementReceived Settlement(
        string provider,
        decimal gross,
        decimal fee,
        decimal net,
        IReadOnlyList<string>? refs = null) => new()
    {
        Provider = provider,
        SettlementId = $"st_{Guid.NewGuid():N}",
        Currency = "TRY",
        GrossAmount = gross,
        FeeAmount = fee,
        NetAmount = net,
        ProviderRefs = refs ?? [$"pi_{Guid.NewGuid():N}"],
        SettledAt = DateTimeOffset.UtcNow
    };

    private static TopupReceived Topup(
        Guid walletId, decimal amount, string provider, string providerRef) => new()
    {
        Provider = provider,
        EventId = $"evt_{Guid.NewGuid():N}",
        LedgerAccountId = walletId,
        Amount = amount,
        Currency = "TRY",
        ProviderRef = providerRef,
        OccurredAt = DateTimeOffset.UtcNow
    };

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, account, "Settlement testi", ct);
    }

    /// <summary>
    /// Sistem hesapları PAYLAŞILIYOR: mutlak bakiye yerine önce/sonra farkı
    /// ölçülüyor, yoksa başka testlerin yazdıkları bu testi düşürürdü.
    /// </summary>
    private async Task<(decimal Balance, long Version)> SnapshotAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var balance = await db.LedgerBalances
            .AsNoTracking()
            .SingleAsync(b => b.LedgerAccountId == accountId, ct);

        return (balance.Balance, balance.Version);
    }

    private async Task<List<LedgerEntry>> EntriesAsync(Guid transactionId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.TransactionId == transactionId)
            .ToListAsync(ct);
    }
}

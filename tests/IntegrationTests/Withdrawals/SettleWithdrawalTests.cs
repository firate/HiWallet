using System.Text.Json.Nodes;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Withdrawals;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Çekimin muhasebesinin kapanması (adım 5.5b,
/// <c>ledger-schema.md</c> "Withdrawal settlement").
///
/// Debit'te clearing'e yazılan borç burada kapanıyor ve para nostro'dan çıkıyor.
/// Bu adım olmadan clearing sessizce dolu kalırdı ve mutabakat her çekimi
/// "settlement'ı gelmemiş" diye raporlardı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SettleWithdrawalTests(PostgresFixture postgres)
{
    private const decimal Amount = 100m;
    private const decimal Commission = 2m;
    private const decimal BankFee = 1.50m;

    private SettleWithdrawalHandler Handler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<SettleWithdrawalHandler>.Instance);

    /// <summary>
    /// <b>Asıl kanıt.</b> bank-fake <c>Invoiced</c> modelde: settlement kaydında
    /// gider bacağı YOK ve nostro'dan yalnızca müşteriye giden tutar çıkıyor.
    /// Ücret dönem sonu faturasıyla ödenecek (madde 10); burada da yazmak gideri
    /// iki kez kaydederdi.
    /// </summary>
    [Fact]
    public async Task InvoicedModel_ClearingKapanir_NostrodanCekimTutariCikar()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await ArrangeDebitAsync(ct);

        var (clearingBefore, _) = await SnapshotAsync(SystemAccounts.ClearingBankTry, ct);
        var (nostroBefore, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);
        var (expenseBefore, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);

        var reply = await Handler().HandleAsync(Settle(sagaId), ct);

        reply.RoutingKey.ShouldBe(nameof(WithdrawalSettled));
        reply.Replayed.ShouldBeFalse();

        var settlement = await SettlementAsync(sagaId, ct);

        settlement.Entries.Count.ShouldBe(2);
        settlement.Entries.Sum(e => e.Amount).ShouldBe(0m);

        // Debit'te clearing +100 yazılmıştı; burada kapanıyor.
        settlement.Entries.Single(e => e.LedgerAccountId == SystemAccounts.ClearingBankTry)
            .Amount.ShouldBe(-Amount);

        // Bankadan çıkan para yalnızca müşterinin tutarı: ücret faturayla ödenecek.
        settlement.Entries.Single(e => e.LedgerAccountId == SystemAccounts.NostroBankTry)
            .Amount.ShouldBe(Amount);

        var (clearingAfter, _) = await SnapshotAsync(SystemAccounts.ClearingBankTry, ct);
        var (nostroAfter, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);
        var (expenseAfter, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);

        (clearingAfter - clearingBefore).ShouldBe(-Amount);
        (nostroAfter - nostroBefore).ShouldBe(Amount);
        expenseAfter.ShouldBe(expenseBefore, "Invoiced modelde gider settlement'ta yazılmaz");
    }

    /// <summary>
    /// Ücret <c>provider_fees</c>'e tahakkuk ediyor ve faturayla kapanmak üzere
    /// AÇIK kalıyor: <c>actual_amount</c> ve <c>invoice_ref</c> boş.
    /// </summary>
    [Fact]
    public async Task BankaUcreti_FaturaBeklemekUzereTahakkukEder()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await ArrangeDebitAsync(ct);

        await Handler().HandleAsync(Settle(sagaId), ct);

        var settlement = await SettlementAsync(sagaId, ct);

        await using var db = postgres.CreateContext();
        var fee = await db.ProviderFees.AsNoTracking()
            .SingleAsync(f => f.TransactionId == settlement.Id, ct);

        fee.Provider.ShouldBe("bank-fake");
        fee.ExpectedAmount.ShouldBe(BankFee);
        fee.ActualAmount.ShouldBeNull("Invoiced modelde gerçekleşen tutar fatura ile belli olur");
        fee.InvoiceRef.ShouldBeNull();
        fee.ProviderRef.ShouldBe("BNK-TEST");
    }

    /// <summary>
    /// Tekrar teslimde ledger'a DOKUNULMUYOR, saklanan cevap yeniden yayınlanıyor
    /// (madde 32). Dokunsaydı clearing iki kez kapanır ve nostro olmayan parayla
    /// eksilirdi.
    /// </summary>
    [Fact]
    public async Task TekrarTeslim_LedgeraDokunmaz_SaklananCevabiDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var sagaId = await ArrangeDebitAsync(ct);
        var command = Settle(sagaId);

        var (before, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        var first = await Handler().HandleAsync(command, ct);
        var second = await Handler().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();

        // Bayt bayt AYNI değil: cevap jsonb kolonunda saklanıyor ve Postgres anahtar
        // sırasını normalize ediyor. Anlamsal eşitlik yeterli — ve tam olarak
        // istenen bu: saga aynı cevabı alıyor.
        JsonNode.DeepEquals(
            JsonNode.Parse(first.Payload), JsonNode.Parse(second.Payload)).ShouldBeTrue();

        var (after, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        (after - before).ShouldBe(Amount, "ikinci teslim ledger'a yazmamalı");
    }

    /// <summary>
    /// Çekim işlemi yoksa settlement yazılamaz: kapatılacak bir borç yok ve
    /// uydurulmuş bir tutar yazmak ledger'ı bozardı.
    /// </summary>
    [Fact]
    public async Task CekimIslemiYoksa_Patlar()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<InvalidOperationException>(
            Handler().HandleAsync(Settle(Guid.NewGuid()), ct));
    }

    private static SettleWithdrawal Settle(Guid sagaId) => new()
    {
        CommandId = Guid.NewGuid(),
        SagaId = sagaId,
        FeeAmount = BankFee,
        BankReference = "BNK-TEST"
    };

    /// <summary>
    /// Çekim debit'ini kurar: cüzdan -102, clearing +100, revenue +2
    /// (CLAUDE.md "Withdrawal saga"). Settlement bunun clearing bacağını kapatıyor.
    /// </summary>
    private async Task<Guid> ArrangeDebitAsync(CancellationToken ct)
    {
        var sagaId = Guid.NewGuid();

        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
        var wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Settle testi", ct);

        await LedgerSeeder.FundAsync(db, wallet, 500m, ct);

        var currency = SystemAccounts.DefaultCurrency;
        var now = DateTimeOffset.UtcNow;

        var tx = LedgerTransaction
            .Create(Guid.NewGuid(), LedgerTransactionType.Withdrawal, wallet, now,
                $"withdrawal:{sagaId}", sagaId)
            .AddEntry(wallet, new Money(-(Amount + Commission), currency))
            .AddEntry(SystemAccounts.ClearingBankTry, new Money(Amount, currency))
            .AddEntry(SystemAccounts.RevenueTry, new Money(Commission, currency));

        tx.AssertBalanced();
        db.LedgerTransactions.Add(tx);

        foreach (var (id, delta) in new[]
                 {
                     (wallet, -(Amount + Commission)),
                     (SystemAccounts.ClearingBankTry, Amount),
                     (SystemAccounts.RevenueTry, Commission)
                 }.OrderBy(x => x.Item1))
        {
            var balance = await db.LedgerBalances.FindAsync([id], ct)
                          ?? throw new InvalidOperationException($"Bakiye satırı yok: {id}");

            balance.Apply(new Money(delta, currency), canGoNegative: id != wallet, now);
        }

        await db.SaveChangesAsync(ct);

        return sagaId;
    }

    private async Task<LedgerTransaction> SettlementAsync(Guid sagaId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.LedgerTransactions
            .AsNoTracking()
            .Include(t => t.Entries)
            .SingleAsync(
                t => t.CorrelationId == sagaId && t.Type == LedgerTransactionType.Settlement, ct);
    }

    /// <summary>Sistem hesapları paylaşılıyor; mutlak bakiye yerine fark ölçülüyor.</summary>
    private async Task<(decimal Balance, long Version)> SnapshotAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var balance = await db.LedgerBalances.AsNoTracking()
            .SingleAsync(b => b.LedgerAccountId == accountId, ct);

        return (balance.Balance, balance.Version);
    }
}

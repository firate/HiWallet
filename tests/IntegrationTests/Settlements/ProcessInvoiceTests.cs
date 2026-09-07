using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Settlements;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Settlements;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Settlements;

/// <summary>
/// Dönem sonu faturası ve uyuşmazlık (adım 5.6, <c>decisions.md</c> madde 11).
///
/// Kuralın özü: fatura beklenen toplamla tutmuyorsa ledger'a HİÇBİR ŞEY yazılmaz.
/// Sistem hangi tarafın haklı olduğuna karar veremez.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProcessInvoiceTests(PostgresFixture postgres)
{
    /// <summary>Invoiced modeldeki sağlayıcı; ücreti dönem sonu faturasıyla alıyor.</summary>
    private const string Bank = "bank-fake";

    private ProcessInvoiceHandler Handler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessInvoiceHandler>.Instance);

    private ProcessTopupHandler TopupHandler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessTopupHandler>.Instance);

    [Fact]
    public async Task TutanFatura_LedgeraYazilir_UcretSatirlariKapanir()
    {
        var ct = TestContext.Current.CancellationToken;

        // İki top-up, her biri 1.50 beklenen ücret üretiyor → toplam 3.00.
        var refs = await ArrangeFeesAsync(count: 2, ct);

        var (expenseBefore, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);
        var (nostroBefore, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        var result = await Handler().HandleAsync(Invoice(amount: 3.00m, refs: refs), ct);

        result.Status.ShouldBe(ProviderInvoiceStatus.Applied);

        var entries = await EntriesAsync(result.LedgerTransactionId!.Value, ct);

        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Amount).ShouldBe(0m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.ProviderExpenseBankTry)
            .Amount.ShouldBe(-3.00m);

        // nostro POZİTİF: varlık hesabı ve bankadan para çıkıyor, yani sıfıra doğru.
        entries.Single(e => e.LedgerAccountId == SystemAccounts.NostroBankTry)
            .Amount.ShouldBe(3.00m);

        var (expenseAfter, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);
        var (nostroAfter, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        (expenseAfter - expenseBefore).ShouldBe(-3.00m);
        (nostroAfter - nostroBefore).ShouldBe(3.00m);

        await using var db = postgres.CreateContext();
        var fees = await db.ProviderFees.AsNoTracking()
            .Where(f => f.ProviderRef != null && refs.Contains(f.ProviderRef))
            .ToListAsync(ct);

        fees.ShouldAllBe(f => f.InvoiceRef != null);
        fees.ShouldAllBe(f => f.LedgerTransactionId == result.LedgerTransactionId);
    }

    /// <summary>
    /// <b>Asıl kural.</b> Tolerans dışı fark → ledger'a hiçbir şey yazılmıyor,
    /// kayıt <c>PendingReview</c>'da bekliyor.
    /// </summary>
    [Fact]
    public async Task TutmayanFatura_LedgeraYazilmaz_IncelemeyeDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = await ArrangeFeesAsync(count: 2, ct);

        var (expenseBefore, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);
        var (nostroBefore, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        // Beklenen 3.00, fatura 50.00 — tolerans (max(0.003, 1)) çok aşılıyor.
        var result = await Handler().HandleAsync(Invoice(amount: 50.00m, refs: refs), ct);

        result.Status.ShouldBe(ProviderInvoiceStatus.PendingReview);
        result.LedgerTransactionId.ShouldBeNull();

        var (expenseAfter, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);
        var (nostroAfter, _) = await SnapshotAsync(SystemAccounts.NostroBankTry, ct);

        expenseAfter.ShouldBe(expenseBefore, "uyuşmazlıkta gider yazılmamalı");
        nostroAfter.ShouldBe(nostroBefore, "uyuşmazlıkta nostro hareket etmemeli");

        await using var db = postgres.CreateContext();

        // Ücret satırlarına DOKUNULMADI: düzeltilmiş fatura geldiğinde yeniden
        // kapsanabilmeleri gerekiyor.
        var fees = await db.ProviderFees.AsNoTracking()
            .Where(f => f.ProviderRef != null && refs.Contains(f.ProviderRef))
            .ToListAsync(ct);

        fees.ShouldAllBe(f => f.InvoiceRef == null);

        // Ama faturanın kendisi kayıtlı: incelenecek bir şey olmadan "bekliyor"
        // demenin anlamı olmazdı.
        var invoice = await db.ProviderInvoices.AsNoTracking()
            .SingleAsync(i => i.Provider == Bank && i.Amount == 50.00m, ct);

        invoice.Status.ShouldBe(ProviderInvoiceStatus.PendingReview);
        invoice.ExpectedAmount.ShouldBe(3.00m);
        invoice.LedgerTransactionId.ShouldBeNull();
        invoice.Note.ShouldNotBeNull();
    }

    /// <summary>
    /// Kuruş farkı kaçınılmaz: sağlayıcı işlem başına yuvarlıyor, biz ondalık
    /// tutuyoruz. Tolerans içindeki fark yazılıyor ve gerekçesi kayda düşüyor.
    /// </summary>
    [Fact]
    public async Task ToleransIcindeFark_YazilirVeNotDuser()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = await ArrangeFeesAsync(count: 2, ct);

        // Beklenen 3.00, tolerans max(0.003, 1) = 1.00. Fark 0.40 → içeride.
        var result = await Handler().HandleAsync(Invoice(amount: 3.40m, refs: refs), ct);

        result.Status.ShouldBe(ProviderInvoiceStatus.Applied);

        var entries = await EntriesAsync(result.LedgerTransactionId!.Value, ct);

        // Yazılan tutar FATURADAKİ: bankadan çıkan para o, beklenen değil.
        entries.Single(e => e.LedgerAccountId == SystemAccounts.ProviderExpenseBankTry)
            .Amount.ShouldBe(-3.40m);

        await using var db = postgres.CreateContext();
        var invoice = await db.ProviderInvoices.AsNoTracking()
            .SingleAsync(i => i.Id != Guid.Empty && i.Amount == 3.40m, ct);

        invoice.Note.ShouldNotBeNull();
    }

    /// <summary>
    /// Aynı faturayı iki kez işlemek doğrudan yanlış gider kaydı — manuel tetiklenen
    /// akışlarda gerçek bir risk (madde 11).
    /// </summary>
    [Fact]
    public async Task AyniFatura_IkinciKezIslenmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = await ArrangeFeesAsync(count: 2, ct);
        var invoice = Invoice(amount: 3.00m, refs: refs);

        var (before, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);

        var first = await Handler().HandleAsync(invoice, ct);
        var second = await Handler().HandleAsync(invoice, ct);

        second.Replayed.ShouldBeTrue();
        second.LedgerTransactionId.ShouldBe(first.LedgerTransactionId);

        var (after, _) = await SnapshotAsync(SystemAccounts.ProviderExpenseBankTry, ct);

        (after - before).ShouldBe(-3.00m, "gider yalnızca bir kez yazılmalı");
    }

    /// <summary>
    /// Net modeldeki sağlayıcıdan fatura gelmesi: ücret settlement anında kesildi,
    /// fatura adımı yok. Ya sağlayıcı modeli değiştirdi ya konfigürasyon eskidi.
    /// </summary>
    [Fact]
    public async Task NetModelSaglayici_FaturaGonderirse_Reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<InvoiceRejectedException>(
            Handler().HandleAsync(
                Invoice(amount: 10m, refs: [], provider: "stripe-fake"), ct));
    }

    /// <summary>
    /// Referans listesi olmayan fatura: kapsam "faturalanmamış hepsi". Bazı
    /// sağlayıcılar yalnızca toplam gönderiyor ve bu bir eksiklik değil, verdikleri
    /// bilginin sınırı.
    /// </summary>
    [Fact]
    public async Task ReferansSizFatura_FaturalanmamislarinTamaminiKapsar()
    {
        var ct = TestContext.Current.CancellationToken;
        var refs = await ArrangeFeesAsync(count: 3, ct);

        await using (var db = postgres.CreateContext())
        {
            // Bu testin başlangıcındaki faturalanmamış toplam, önceki testlerin
            // bıraktıklarını da içeriyor; beklenen tutar oradan hesaplanıyor.
            var expected = await db.ProviderFees
                .Where(f => f.Provider == Bank && f.InvoiceRef == null)
                .SumAsync(f => f.ExpectedAmount, ct);

            var result = await Handler().HandleAsync(Invoice(expected, refs: []), ct);

            result.Status.ShouldBe(ProviderInvoiceStatus.Applied);
        }

        await using var read = postgres.CreateContext();
        var stillOpen = await read.ProviderFees.AsNoTracking()
            .CountAsync(f => f.ProviderRef != null && refs.Contains(f.ProviderRef)
                             && f.InvoiceRef == null, ct);

        stillOpen.ShouldBe(0);
    }

    /// <summary>
    /// Fatura ücret satırlarını kapatıyor ama <c>actual_amount</c> DOLDURULMUYOR:
    /// fatura toplam tutarı bildiriyor, satır başına dağıtmak uydurma bir hassasiyet
    /// olurdu (settlement tarafındaki aynı gerekçe).
    /// </summary>
    private static ProviderInvoiceReceived Invoice(
        decimal amount, IReadOnlyList<string> refs, string provider = Bank) => new()
    {
        Provider = provider,
        InvoiceRef = $"inv_{Guid.NewGuid():N}",
        Currency = "TRY",
        Amount = amount,
        ProviderRefs = refs,
        IssuedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Fatura kapsayacak ücret satırı üretir: bank-fake üzerinden top-up, her biri
    /// 1.50 sabit ücret beklentisi doğuruyor.
    /// </summary>
    private async Task<string[]> ArrangeFeesAsync(int count, CancellationToken ct)
    {
        Guid wallet;

        await using (var db = postgres.CreateContext())
        {
            var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            wallet = await LedgerSeeder.CreateWalletAsync(db, account, "Fatura testi", ct);
        }

        var refs = new string[count];

        for (var i = 0; i < count; i++)
        {
            refs[i] = $"pi_{Guid.NewGuid():N}";

            await TopupHandler().HandleAsync(new TopupReceived
            {
                Provider = Bank,
                EventId = $"evt_{Guid.NewGuid():N}",
                LedgerAccountId = wallet,
                Amount = 100m,
                Currency = "TRY",
                ProviderRef = refs[i],
                OccurredAt = DateTimeOffset.UtcNow
            }, ct);
        }

        return refs;
    }

    private async Task<(decimal Balance, long Version)> SnapshotAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var balance = await db.LedgerBalances.AsNoTracking()
            .SingleAsync(b => b.LedgerAccountId == accountId, ct);

        return (balance.Balance, balance.Version);
    }

    private async Task<List<HiWallet.WalletService.Domain.Ledger.LedgerEntry>> EntriesAsync(
        Guid transactionId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.LedgerEntries.AsNoTracking()
            .Where(e => e.TransactionId == transactionId)
            .ToListAsync(ct);
    }
}

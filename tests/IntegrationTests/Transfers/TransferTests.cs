using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Transfers;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using HiWallet.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Transfers;

[Collection(PostgresCollection.Name)]
public sealed class TransferTests(PostgresFixture postgres)
{
    private static readonly Currency Try = SystemAccounts.DefaultCurrency;

    private static CreateTransferHandler Handler(
        PostgresFixture postgres,
        CommissionRate? paymentRate = null,
        TransferLimit? p2pLimit = null)
    {
        var commissions = new Dictionary<TransferType, CommissionRate>();
        if (paymentRate is not null)
        {
            commissions[TransferType.Payment] = paymentRate;
        }

        var limits = new Dictionary<TransferType, TransferLimit>();
        if (p2pLimit is not null)
        {
            limits[TransferType.P2P] = p2pLimit;
        }

        return new CreateTransferHandler(
            postgres.ContextFactory,
            new LimitPolicy(limits),
            new CommissionPolicy(commissions),
            new SystemClock(),
            NullLogger<CreateTransferHandler>.Instance);
    }

    // ------------------------------------------------------------------
    // structure.md'nin "ilk yazılacak test"i.
    // ------------------------------------------------------------------
    [Fact]
    public async Task EszamanliTransferler_ZeroSumKorunur_VeHicbirCuzdanNegatifDusmez()
    {
        var ct = TestContext.Current.CancellationToken;
        const int walletCount = 10;
        const int transferCount = 500;
        const decimal initialFunds = 1_000m;

        var wallets = new List<Guid>();

        await using (var db = postgres.CreateContext())
        {
            for (var i = 0; i < walletCount; i++)
            {
                var accountId = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
                var walletId = await LedgerSeeder.CreateWalletAsync(db, accountId, $"Cüzdan {i}", ct);
                await LedgerSeeder.FundAsync(db, walletId, initialFunds, ct);
                wallets.Add(walletId);
            }
        }

        var handler = Handler(postgres);
        var random = new Random(20260904);

        var transfers = Enumerable.Range(0, transferCount).Select(_ =>
        {
            var from = random.Next(walletCount);
            var to = (from + 1 + random.Next(walletCount - 1)) % walletCount;
            var amount = random.Next(1, 50);
            return new CreateTransferCommand(
                wallets[from], wallets[to], amount, Try.Code, TransferType.P2P);
        }).ToArray();

        // Hepsi aynı anda. Aynı cüzdana yazan transferler optimistic lock'a takılacak
        // ve retry'a düşecek — kanıtlanmak istenen şey bu yükte invariant'ın korunması.
        var results = await Task.WhenAll(transfers.Select(async t =>
        {
            try
            {
                await handler.HandleAsync(t, ct);
                return "ok";
            }
            catch (InsufficientFundsException)
            {
                // İş kuralı reddi → 422. Para hareketi olmadı.
                return "yetersiz";
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry tükendi → 409 (decisions.md madde 9). Bu da geçerli bir sonuç;
                // rollback oldu, hiçbir şey yazılmadı. Testin iddiası "her transfer
                // başarılı olur" değil, "ne olursa olsun invariant korunur".
                return "cakisma";
            }
        }));

        var succeeded = results.Count(r => r == "ok");
        succeeded.ShouldBeGreaterThan(0, "hiçbir transfer geçmediyse test bir şey kanıtlamıyor");

        await using var verify = postgres.CreateContext();

        // 1) Her ledger transaction'ının toplamı sıfır.
        var unbalanced = await verify.LedgerEntries
            .GroupBy(e => e.TransactionId)
            .Where(g => g.Sum(e => e.Amount) != 0m)
            .Select(g => g.Key)
            .ToListAsync(ct);

        unbalanced.ShouldBeEmpty("dengesiz ledger transaction'ı var");

        // 2) Sistem genelinde toplam sıfır — para yoktan var olmadı, yok olmadı.
        var total = await verify.LedgerEntries.SumAsync(e => e.Amount, ct);
        total.ShouldBe(0m);

        // 3) Hiçbir cüzdan negatif değil.
        var negativeWallets = await verify.LedgerBalances
            .Join(verify.LedgerAccounts, b => b.LedgerAccountId, a => a.Id, (b, a) => new { b, a })
            .Where(x => x.a.Type == LedgerAccountType.UserWallet && x.b.Balance < 0m)
            .Select(x => x.b.LedgerAccountId)
            .ToListAsync(ct);

        negativeWallets.ShouldBeEmpty("negatif bakiyeli cüzdan var");

        // 4) Projeksiyon ledger'dan sapmadı (mutabakat sorgusunun kendisi).
        var drifted = await verify.LedgerBalances
            .Where(b => b.Balance != (verify.LedgerEntries
                .Where(e => e.LedgerAccountId == b.LedgerAccountId)
                .Sum(e => (decimal?)e.Amount) ?? 0m))
            .Select(b => b.LedgerAccountId)
            .ToListAsync(ct);

        drifted.ShouldBeEmpty("bakiye projeksiyonu ledger'dan sapmış");
    }

    [Fact]
    public async Task Transfer_YetersizBakiye_Reddeder_VeHicYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid from, to;

        await using (var db = postgres.CreateContext())
        {
            var a1 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var a2 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            from = await LedgerSeeder.CreateWalletAsync(db, a1, "Az", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, a2, "Alıcı", ct);
            await LedgerSeeder.FundAsync(db, from, 10m, ct);
        }

        var handler = Handler(postgres);

        await Should.ThrowAsync<InsufficientFundsException>(() => handler.HandleAsync(
            new CreateTransferCommand(from, to, 100m, Try.Code, TransferType.P2P), ct));

        await using var verify = postgres.CreateContext();

        // "Hiç yazma": reddedilen transfer ledger'a satır bırakmamalı.
        var entryCount = await verify.LedgerEntries.CountAsync(e => e.LedgerAccountId == to, ct);
        entryCount.ShouldBe(0);

        var balance = await verify.LedgerBalances.SingleAsync(b => b.LedgerAccountId == from, ct);
        balance.Balance.ShouldBe(10m);
    }

    [Fact]
    public async Task Transfer_AyniIdempotencyKey_YeniTransferYapmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid from, to;

        await using (var db = postgres.CreateContext())
        {
            var a1 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var a2 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            from = await LedgerSeeder.CreateWalletAsync(db, a1, "Gönderen", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, a2, "Alıcı", ct);
            await LedgerSeeder.FundAsync(db, from, 500m, ct);
        }

        var handler = Handler(postgres);
        var command = new CreateTransferCommand(
            from, to, 100m, Try.Code, TransferType.P2P, IdempotencyKey: "tekrar-eden-istek");

        var first = await handler.HandleAsync(command, ct);
        var second = await handler.HandleAsync(command, ct);

        first.Replayed.ShouldBeFalse();
        second.Replayed.ShouldBeTrue();
        second.TransactionId.ShouldBe(first.TransactionId);

        await using var verify = postgres.CreateContext();
        var balance = await verify.LedgerBalances.SingleAsync(b => b.LedgerAccountId == from, ct);
        balance.Balance.ShouldBe(400m, "para iki kez düşülmüş");
    }

    [Fact]
    public async Task Transfer_LimitAsimindanSonraTekrar_LimitDegilMevcutIslemiDoner()
    {
        // Idempotency kapısı policy'den ÖNCE olmalı: ilk transfer limiti doldurduysa,
        // aynı isteğin tekrarı 422 değil orijinal işlemi dönmeli.
        var ct = TestContext.Current.CancellationToken;
        Guid from, to;

        await using (var db = postgres.CreateContext())
        {
            var a1 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var a2 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            from = await LedgerSeeder.CreateWalletAsync(db, a1, "Gönderen", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, a2, "Alıcı", ct);
            await LedgerSeeder.FundAsync(db, from, 500m, ct);
        }

        var handler = Handler(postgres, p2pLimit: new TransferLimit(Daily: 100m));
        var command = new CreateTransferCommand(
            from, to, 100m, Try.Code, TransferType.P2P, IdempotencyKey: "limiti-dolduran");

        var first = await handler.HandleAsync(command, ct);
        var second = await handler.HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        second.TransactionId.ShouldBe(first.TransactionId);
    }

    [Fact]
    public async Task Transfer_KomisyonluTip_UcBacakYazar_VeGelirHesabinaGider()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid from, to;

        await using (var db = postgres.CreateContext())
        {
            var person = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var business = await LedgerSeeder.CreateAccountAsync(db, AccountType.Business, ct);
            from = await LedgerSeeder.CreateWalletAsync(db, person, "Müşteri", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, business, "Dükkan", ct);
            await LedgerSeeder.FundAsync(db, from, 500m, ct);
        }

        var handler = Handler(postgres, paymentRate: new CommissionRate(0.02m));

        var result = await handler.HandleAsync(
            new CreateTransferCommand(from, to, 100m, Try.Code, TransferType.Payment), ct);

        await using var verify = postgres.CreateContext();

        var entries = await verify.LedgerEntries
            .Where(e => e.TransactionId == result.TransactionId)
            .ToListAsync(ct);

        // overview.md madde 4: gönderen -102, alan +100, revenue +2, toplam 0.
        entries.Count.ShouldBe(3);
        entries.Single(e => e.LedgerAccountId == from).Amount.ShouldBe(-102m);
        entries.Single(e => e.LedgerAccountId == to).Amount.ShouldBe(100m);
        entries.Single(e => e.LedgerAccountId == SystemAccounts.RevenueTry).Amount.ShouldBe(2m);
        entries.Sum(e => e.Amount).ShouldBe(0m);

        var revenue = await verify.LedgerBalances
            .SingleAsync(b => b.LedgerAccountId == SystemAccounts.RevenueTry, ct);
        revenue.Balance.ShouldBe(2m);
    }

    [Fact]
    public async Task Transfer_LimitAsimi_Reddeder()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid from, to;

        await using (var db = postgres.CreateContext())
        {
            var a1 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var a2 = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            from = await LedgerSeeder.CreateWalletAsync(db, a1, "Gönderen", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, a2, "Alıcı", ct);
            await LedgerSeeder.FundAsync(db, from, 5_000m, ct);
        }

        var handler = Handler(postgres, p2pLimit: new TransferLimit(PerTransaction: 100m));

        await Should.ThrowAsync<LimitExceededException>(() => handler.HandleAsync(
            new CreateTransferCommand(from, to, 101m, Try.Code, TransferType.P2P), ct));

        await using var verify = postgres.CreateContext();
        var balance = await verify.LedgerBalances.SingleAsync(b => b.LedgerAccountId == from, ct);
        balance.Balance.ShouldBe(5_000m);
    }

    [Fact]
    public async Task Transfer_LimitIkinciCuzdanlaAsilamaz()
    {
        // Kapsam hesap: aynı hesabın iki cüzdanından yapılan transferler tek limite
        // yazılır (decisions.md madde 20).
        var ct = TestContext.Current.CancellationToken;
        Guid first, second, to;

        await using (var db = postgres.CreateContext())
        {
            var sender = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            var receiver = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);
            first = await LedgerSeeder.CreateWalletAsync(db, sender, "Birinci", ct);
            second = await LedgerSeeder.CreateWalletAsync(db, sender, "İkinci", ct);
            to = await LedgerSeeder.CreateWalletAsync(db, receiver, "Alıcı", ct);
            await LedgerSeeder.FundAsync(db, first, 500m, ct);
            await LedgerSeeder.FundAsync(db, second, 500m, ct);
        }

        var handler = Handler(postgres, p2pLimit: new TransferLimit(Daily: 100m));

        await handler.HandleAsync(
            new CreateTransferCommand(first, to, 100m, Try.Code, TransferType.P2P), ct);

        // İkinci cüzdandan devam etmek limiti aşmalı — cüzdan bazında olsaydı geçerdi.
        await Should.ThrowAsync<LimitExceededException>(() => handler.HandleAsync(
            new CreateTransferCommand(second, to, 1m, Try.Code, TransferType.P2P), ct));
    }
}

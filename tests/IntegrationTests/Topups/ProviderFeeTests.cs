using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.CardTopups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Kart sağlayıcısının ücretinin tahakkuku (adım 5.4, <c>decisions.md</c> madde 10).
/// Kartla yükleme ödendiğinde ücret beklentisi ledger'la aynı transaction'da yazılıyor.
///
/// Ücret bizim müşteriden aldığımız komisyon DEĞİL, sağlayıcının bizden aldığı
/// ücret — biri gelir, öbürü gider, netleştirilmiyorlar.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProviderFeeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task KartYuklemesi_UcretSatiriYazar_OranVeSabitiUygular()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var transactionId = await CardTopupSeeder.PaidAsync(postgres, wallet, 100m, ct);

        var fee = await ReadFeeAsync(transactionId, ct);

        fee.ShouldNotBeNull();
        fee.Provider.ShouldBe("stripe-fake");
        fee.SettlementModel.ShouldBe(FeeSettlement.Net);

        // 100 * 0.029 + 0.30 = 3.20
        fee.ExpectedAmount.ShouldBe(3.20m);
        fee.Currency.ShouldBe("TRY");
    }

    /// <summary>
    /// Gerçekleşen tutar HENÜZ bilinmiyor: Net modelde settlement'ta belli olacak. Sıfır
    /// yazılsaydı "ücret alınmadı" ile "henüz bilmiyoruz" ayrımı kaybolurdu.
    /// </summary>
    [Fact]
    public async Task Tahakkuk_AnindaGerceklesenTutarBosKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var transactionId = await CardTopupSeeder.PaidAsync(postgres, wallet, 250m, ct);
        var fee = await ReadFeeAsync(transactionId, ct);

        fee.ShouldNotBeNull();
        fee.ActualAmount.ShouldBeNull();
        fee.InvoiceRef.ShouldBeNull();
        fee.LedgerTransactionId.ShouldBeNull();
    }

    /// <summary>
    /// <b>Asıl kanıt.</b> <c>expected_amount</c> bir tahmin ve ledger'a ASLA
    /// yazılmıyor. Yazılsaydı ledger'ın "burada yazan her şey gerçekleşmiştir"
    /// özelliği giderdi ve zero-sum toplamı bozulurdu.
    /// </summary>
    [Fact]
    public async Task BeklenenUcret_LedgeraYazilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var transactionId = await CardTopupSeeder.PaidAsync(postgres, wallet, 100m, ct);

        await using var db = postgres.CreateContext();

        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.TransactionId == transactionId)
            .ToListAsync(ct);

        // Yalnızca iki bacak: cüzdan +100, clearing -100. provider_expense YOK —
        // o bacak settlement'ta doğacak (5.5), tahakkukta değil.
        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Amount).ShouldBe(0m);
        entries.ShouldNotContain(e => e.Amount == 3.20m || e.Amount == -3.20m);
    }

    /// <summary>
    /// Tekrar eden kapanış ikinci ücret satırı yazmamalı. Yazsaydı gider beklentisi her
    /// yeniden teslimde şişer ve fatura karşılaştırması (madde 11) yanlış tarafı suçlardı.
    /// </summary>
    [Fact]
    public async Task TekrarEdenKapanis_IkinciUcretSatiriYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var id = await PlaceAsync(wallet, 100m, ct);
        var message = Paid(id, 100m);

        var first = await Handler(TestProviders.Policy).HandleAsync(message, ct);
        var second = await Handler(TestProviders.Policy).HandleAsync(message, ct);

        second.Replayed.ShouldBeTrue();

        await using var db = postgres.CreateContext();
        var fees = await db.ProviderFees
            .AsNoTracking()
            .Where(f => f.TransactionId == first.LedgerTransactionId!.Value)
            .ToListAsync(ct);

        fees.Count.ShouldBe(1);
    }

    /// <summary>
    /// Clearing hesabı OLAN ama tarifesi yazılmamış sağlayıcı: ücretsiz sayılmıyor,
    /// patlıyor. Sessizce sıfır ücret varsaymak o sağlayıcının bütün giderini
    /// raporlardan siler ve fatura geldiğinde uyuşmazlık üretirdi (madde 11).
    ///
    /// <see cref="CardTopupRejectedException"/> DEĞİL, bilerek: o kalıcı hata sayılıp
    /// dead-letter'a gidiyor. Kendi konfigürasyon eksiğimiz yüzünden müşterinin para
    /// yükleme bildirimini çöpe atmak yanlış olur — mesaj kuyrukta beklesin, tarife
    /// eklenince işlensin. Bekleyen yükleme yazılmadı: pay açık kalıyor.
    /// </summary>
    [Fact]
    public async Task TarifesiOlmayanSaglayici_Patlar_YuklemeAcikKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var id = await PlaceAsync(wallet, 100m, ct);

        // Yalnızca bank-fake tanımlı; stripe-fake'in clearing hesabı var ama tarifesi yok.
        var eksikPolitika = new ProviderPolicy(new Dictionary<string, ProviderTerms>
        {
            ["bank-fake"] = new(
                "bank-fake", FeeSettlement.Invoiced, new ProviderFeeTariff(Rate: 0m, 1.50m),
                FundType.Cash)
        });

        var exception = await Should.ThrowAsync<UnknownProviderException>(
            Handler(eksikPolitika).HandleAsync(Paid(id, 100m), ct));

        exception.Provider.ShouldBe("stripe-fake");

        await using var db = postgres.CreateContext();
        (await db.CardTopupHoldClosures.AnyAsync(c => c.HoldId == id, ct)).ShouldBeFalse();

        // Tarife eklenince aynı mesaj işleniyor.
        var result = await Handler(TestProviders.Policy).HandleAsync(Paid(id, 100m), ct);
        result.Replayed.ShouldBeFalse();
    }

    /// <summary>
    /// Ücret satırı ledger işlemine FK ile bağlı: hedefi olmayan bir satır bozulma.
    /// processed_events'in aksine burada FK var, gerekçesi migration'da.
    /// </summary>
    [Fact]
    public async Task OlmayanIsleme_UcretSatiriYazilamaz()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var db = postgres.CreateContext();

        db.ProviderFees.Add(new ProviderFee
        {
            Id = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            Provider = "stripe-fake",
            SettlementModel = FeeSettlement.Net,
            ExpectedAmount = 1m,
            Currency = "TRY",
            OccurredAt = DateTimeOffset.UtcNow
        });

        var exception = await Should.ThrowAsync<DbUpdateException>(db.SaveChangesAsync(ct));

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    private ProcessCardTopupHandler Handler(ProviderPolicy providers) => new(
        postgres.ContextFactory, providers, new SystemClock(), NullLogger<ProcessCardTopupHandler>.Instance);

    private async Task<Guid> PlaceAsync(Guid wallet, decimal amount, CancellationToken ct)
    {
        var id = Guid.NewGuid();

        await new PlaceCardTopupHoldHandler(
                postgres.ContextFactory, TestKycLimits.Policy, new SystemClock(), NullLogger<PlaceCardTopupHoldHandler>.Instance)
            .HandleAsync(new PlaceCardTopupHoldCommand(id, wallet, amount, "TRY", CardTopupSeeder.Provider), ct);

        return id;
    }

    private static CardTopupClosed Paid(Guid id, decimal amount) => new()
    {
        CardTopupId = id,
        Outcome = CardTopupClosedOutcomes.Paid,
        Provider = CardTopupSeeder.Provider,
        ProviderRef = $"pay_{id:N}",
        Amount = amount,
        Currency = "TRY",
        ClosedAt = DateTimeOffset.UtcNow
    };

    private async Task<Guid> NewWalletAsync(CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        var account = await LedgerSeeder.CreateAccountAsync(db, AccountType.Person, ct);

        return await LedgerSeeder.CreateWalletAsync(db, account, "Ücret testi", ct);
    }

    private async Task<ProviderFee?> ReadFeeAsync(Guid transactionId, CancellationToken ct)
    {
        await using var db = postgres.CreateContext();

        return await db.ProviderFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TransactionId == transactionId, ct);
    }
}

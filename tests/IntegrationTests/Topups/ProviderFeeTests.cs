using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Topups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.Topups;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Sağlayıcı ücretinin tahakkuku (adım 5.4, <c>decisions.md</c> madde 10).
///
/// Ücret bizim müşteriden aldığımız komisyon DEĞİL, sağlayıcının bizden aldığı
/// ücret — biri gelir, öbürü gider, netleştirilmiyorlar.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProviderFeeTests(PostgresFixture postgres)
{
    private ProcessTopupHandler Handler() => new(
        postgres.ContextFactory,
        TestProviders.Policy,
        new SystemClock(),
        NullLogger<ProcessTopupHandler>.Instance);

    [Fact]
    public async Task Topup_UcretSatiriYazar_OranVeSabitiUygular()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var result = await Handler().HandleAsync(Message(wallet, 100m, "stripe-fake"), ct);

        var fee = await ReadFeeAsync(result.LedgerTransactionId!.Value, ct);

        fee.ShouldNotBeNull();
        fee.Provider.ShouldBe("stripe-fake");
        fee.SettlementModel.ShouldBe(FeeSettlement.Net);

        // 100 * 0.029 + 0.30 = 3.20
        fee.ExpectedAmount.ShouldBe(3.20m);
        fee.Currency.ShouldBe("TRY");
    }

    /// <summary>
    /// Gerçekleşen tutar HENÜZ bilinmiyor: Net modelde settlement'ta, Invoiced
    /// modelde faturada belli olacak. Sıfır yazılsaydı "ücret alınmadı" ile "henüz
    /// bilmiyoruz" ayrımı kaybolurdu.
    /// </summary>
    [Fact]
    public async Task Tahakkuk_AninadaGerceklesenTutarBosKalir()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var result = await Handler().HandleAsync(Message(wallet, 250m, "stripe-fake"), ct);
        var fee = await ReadFeeAsync(result.LedgerTransactionId!.Value, ct);

        fee.ShouldNotBeNull();
        fee.ActualAmount.ShouldBeNull();
        fee.InvoiceRef.ShouldBeNull();
        fee.LedgerTransactionId.ShouldBeNull();
    }

    /// <summary>
    /// Sağlayıcı bazında model. İki sağlayıcının aynı sistemde farklı modelle
    /// çalışması bu tasarımın göstermek istediği şey (madde 10).
    /// </summary>
    [Fact]
    public async Task ModelSaglayiciBazinda_BankaFaturali()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        var result = await Handler().HandleAsync(Message(wallet, 500m, "bank-fake"), ct);
        var fee = await ReadFeeAsync(result.LedgerTransactionId!.Value, ct);

        fee.ShouldNotBeNull();
        fee.SettlementModel.ShouldBe(FeeSettlement.Invoiced);

        // Oran sıfır, yalnızca sabit ücret: 1.50.
        fee.ExpectedAmount.ShouldBe(TestProviders.BankFixed);
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

        var result = await Handler().HandleAsync(Message(wallet, 100m, "stripe-fake"), ct);

        await using var db = postgres.CreateContext();

        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.TransactionId == result.LedgerTransactionId!.Value)
            .ToListAsync(ct);

        // Yalnızca iki bacak: cüzdan +100, clearing -100. provider_expense YOK —
        // o bacak settlement'ta doğacak (5.5), tahakkukta değil.
        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Amount).ShouldBe(0m);
        entries.ShouldNotContain(e => e.Amount == 3.20m || e.Amount == -3.20m);
    }

    /// <summary>
    /// Tekrar eden webhook ikinci ücret satırı yazmamalı. Yazsaydı gider beklentisi
    /// her yeniden teslimde şişer ve fatura karşılaştırması (madde 11) yanlış
    /// tarafı suçlardı.
    /// </summary>
    [Fact]
    public async Task TekrarEdenTopup_IkinciUcretSatiriYazmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);
        var message = Message(wallet, 100m, "stripe-fake");

        var first = await Handler().HandleAsync(message, ct);
        var second = await Handler().HandleAsync(message, ct);

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
    /// Asıl risk bu, "hiç tanınmayan sağlayıcı" değil: onun clearing hesabı da yok
    /// ve akış daha önce <see cref="TopupRejectedException"/> ile reddediyor.
    /// Buradaki durum ise BİZİM konfigürasyon hatamız — sağlayıcı ledger'da tanımlı,
    /// tarife unutulmuş.
    ///
    /// <see cref="TopupRejectedException"/> DEĞİL, bilerek: o kalıcı hata sayılıp
    /// dead-letter'a gidiyor. Kendi konfigürasyon eksiğimiz yüzünden müşterinin
    /// para yükleme bildirimini çöpe atmak yanlış olur — mesaj kuyrukta beklesin,
    /// tarife eklenince işlensin.
    /// </summary>
    [Fact]
    public async Task TarifesiOlmayanSaglayici_Patlar()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = await NewWalletAsync(ct);

        // Yalnızca bank-fake tanımlı; stripe-fake'in clearing hesabı var ama
        // tarifesi yok.
        var eksikPolitika = new ProviderPolicy(new Dictionary<string, ProviderTerms>
        {
            ["bank-fake"] = new(
                "bank-fake", FeeSettlement.Invoiced, new ProviderFeeTariff(Rate: 0m, 1.50m))
        });

        var handler = new ProcessTopupHandler(
            postgres.ContextFactory,
            eksikPolitika,
            new SystemClock(),
            NullLogger<ProcessTopupHandler>.Instance);

        var exception = await Should.ThrowAsync<UnknownProviderException>(
            handler.HandleAsync(Message(wallet, 100m, "stripe-fake"), ct));

        exception.Provider.ShouldBe("stripe-fake");
        exception.ShouldNotBeAssignableTo<TopupRejectedException>();
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

    private static TopupReceived Message(Guid walletId, decimal amount, string provider) => new()
    {
        Provider = provider,
        EventId = $"evt_{Guid.NewGuid():N}",
        LedgerAccountId = walletId,
        Amount = amount,
        Currency = "TRY",
        ProviderRef = $"pi_{Guid.NewGuid():N}",
        OccurredAt = DateTimeOffset.UtcNow
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

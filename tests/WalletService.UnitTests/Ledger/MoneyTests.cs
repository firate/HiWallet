using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.UnitTests.Ledger;

public sealed class MoneyTests
{
    private static readonly Currency Try = Currency.From("TRY");
    private static readonly Currency Usd = Currency.From("USD");
    private static readonly Currency Jpy = Currency.From("JPY", 0);

    [Theory]
    [InlineData("0.00")]
    [InlineData("100.50")]
    [InlineData("-102.99")]
    public void Ctor_MinorUnitIcindeTutar_KabulEder(string amount)
    {
        var money = new Money(decimal.Parse(amount), Try);

        money.Amount.ShouldBe(decimal.Parse(amount));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.9666")]
    [InlineData("100.005")]
    public void Ctor_KurusunAltindaTutar_Reddeder(string amount)
    {
        // Ledger'a yazılan her tutar müşterinin gerçekten tutabileceği ve çekebileceği
        // bir şey olmalı; kuruşun altı bankaya gönderilemez (decisions.md madde 19).
        Should.Throw<ArgumentException>(() => _ = new Money(decimal.Parse(amount), Try));
    }

    [Fact]
    public void Ctor_MinorUnitiSifirOlanParaBirimi_OndalikKabulEtmez()
    {
        // Basamak sayısı 2'ye gömülü değil, currency'den geliyor.
        Should.NotThrow(() => _ = new Money(100m, Jpy));
        Should.Throw<ArgumentException>(() => _ = new Money(100.5m, Jpy));
    }

    [Fact]
    public void Toplama_FarkliParaBirimi_Reddeder()
    {
        // Zero-sum trigger'ı DB tarafında currency başına ayrı topluyor; burası aynı
        // korumanın uygulama tarafındaki karşılığı.
        Should.Throw<InvalidOperationException>(() => _ = new Money(100m, Try) + new Money(100m, Usd));
    }

    [Fact]
    public void Karsilastirma_FarkliParaBirimi_Reddeder()
    {
        Should.Throw<InvalidOperationException>(() => _ = new Money(100m, Try) > new Money(1m, Usd));
    }

    [Fact]
    public void Isaret_CreditArti_DebitEksi()
    {
        // Konvansiyon hiçbir yerde tersine çevrilmez (CLAUDE.md).
        new Money(100m, Try).IsCredit.ShouldBeTrue();
        new Money(-100m, Try).IsDebit.ShouldBeTrue();
        Money.Zero(Try).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Negated_IsaretiCevirir_TutariKorur()
    {
        var debit = new Money(-102.50m, Try);

        debit.Negated.ShouldBe(new Money(102.50m, Try));
    }

    [Fact]
    public void Default_KullanilamazDurum_Patlar()
    {
        // default(Currency) initialize edilmemiş bir struct; sessizce "" dönmemeli.
        Should.Throw<InvalidOperationException>(() => _ = default(Currency).Code);
    }
}

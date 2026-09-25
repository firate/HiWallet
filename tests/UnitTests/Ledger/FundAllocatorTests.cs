using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.UnitTests.Ledger;

/// <summary>
/// Transferin kovalara dağıtımı (decisions.md madde 36). Sıra sabit:
/// <c>promo</c> → <c>card</c> → <c>cash</c>, ve <c>promo</c> transferde hiç yer
/// almadığı için fiilen <c>card</c> → <c>cash</c>.
///
/// Önce tutar dağıtılıyor, sonra komisyon kalanlara aynı sırayla. Oransal bölmek
/// yerine bu seçildi: kova başına zero-sum korunuyor ve kuruş yuvarlaması dengeyi
/// bozmuyor.
/// </summary>
public sealed class FundAllocatorTests
{
    private static readonly Currency Try = Currency.From("TRY");

    private static Money M(decimal amount) => new(amount, Try);

    private static readonly Guid Wallet = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IReadOnlyDictionary<FundType, Money> Available(
        decimal cash = 0m, decimal card = 0m, decimal promo = 0m) =>
        new Dictionary<FundType, Money>
        {
            [FundType.Cash] = M(cash),
            [FundType.Card] = M(card),
            [FundType.Promo] = M(promo)
        };

    [Fact]
    public void TekKovaYetiyorsa_TekBacakCikar()
    {
        var result = FundAllocator.ForTransfer(Wallet, Available(cash: 500m), M(100m), M(2m));

        result.Count.ShouldBe(1);
        result[0].FundType.ShouldBe(FundType.Cash);
        result[0].Amount.ShouldBe(M(100m));
        result[0].Commission.ShouldBe(M(2m));
    }

    /// <summary>
    /// Kart kovası önce eriyor: en kısıtlı olan önce harcanıyor, nakit korunuyor.
    /// </summary>
    [Fact]
    public void KartVeNakitVarsa_OnceKartEriyor()
    {
        var result = FundAllocator.ForTransfer(Wallet, Available(cash: 500m, card: 30m), M(100m), M(0m));

        result.Count.ShouldBe(2);

        result[0].FundType.ShouldBe(FundType.Card);
        result[0].Amount.ShouldBe(M(30m));

        result[1].FundType.ShouldBe(FundType.Cash);
        result[1].Amount.ShouldBe(M(70m));
    }

    /// <summary>
    /// Tutar kartı bitirdiyse komisyon nakitten çıkıyor. Kova bazında denge şart:
    /// her bacakta gönderenden çıkan, alıcıya giden artı komisyon kadar.
    /// </summary>
    [Fact]
    public void KomisyonTutardanSonra_KalanKovalaraDagiliyor()
    {
        var result = FundAllocator.ForTransfer(Wallet, Available(cash: 500m, card: 30m), M(100m), M(5m));

        result[0].FundType.ShouldBe(FundType.Card);
        result[0].Amount.ShouldBe(M(30m));
        result[0].Commission.ShouldBe(M(0m), "kart kovası tutarla tamamen tükendi");

        result[1].FundType.ShouldBe(FundType.Cash);
        result[1].Amount.ShouldBe(M(70m));
        result[1].Commission.ShouldBe(M(5m));
    }

    /// <summary>
    /// Tutar kartı bitirmediyse komisyon da aynı kovadan devam ediyor.
    /// </summary>
    [Fact]
    public void KartKovasiTutardanSonraDoluysa_KomisyonDaOradanCikiyor()
    {
        var result = FundAllocator.ForTransfer(Wallet, Available(cash: 500m, card: 200m), M(100m), M(5m));

        result.Count.ShouldBe(1);
        result[0].FundType.ShouldBe(FundType.Card);
        result[0].Amount.ShouldBe(M(100m));
        result[0].Commission.ShouldBe(M(5m));
    }

    /// <summary>
    /// Hediye bakiye transferde HİÇ yer almıyor: karşılığında fon yatırılmamış bir
    /// bakiye başkasına geçebilseydi fiilen nakde yakın bir araca dönerdi.
    /// </summary>
    [Fact]
    public void PromoBakiyesi_TransferdeKullanilmiyor()
    {
        var exception = Should.Throw<InsufficientFundsException>(
            () => FundAllocator.ForTransfer(Wallet, Available(promo: 1_000m), M(100m), M(0m)));

        exception.Available.ShouldBe(M(0m), "transfer edilebilir bakiye sıfır");
        exception.Requested.ShouldBe(M(100m));
    }

    /// <summary>
    /// Toplam bakiye yetse bile transfer edilebilir kısım yetmiyorsa reddediliyor.
    /// Müşterinin gördüğü toplamla çıkabilen tutar ayrışıyor; cevap 422.
    /// </summary>
    [Fact]
    public void ToplamYetsede_TransferEdilebilirKisimYetmiyorsa_Reddediliyor()
    {
        var exception = Should.Throw<InsufficientFundsException>(
            () => FundAllocator.ForTransfer(
                Wallet, Available(cash: 10m, card: 20m, promo: 500m), M(100m), M(0m)));

        exception.Available.ShouldBe(M(30m));
        exception.Requested.ShouldBe(M(100m));
    }

    /// <summary>
    /// Komisyon dahil edildiğinde yetmiyorsa da reddediliyor — limit tutara değil
    /// cüzdandan ÇIKAN toplama uygulanıyor.
    /// </summary>
    [Fact]
    public void KomisyonlaBirlikteYetmiyorsa_Reddediliyor()
    {
        var exception = Should.Throw<InsufficientFundsException>(
            () => FundAllocator.ForTransfer(Wallet, Available(cash: 100m), M(100m), M(2m)));

        exception.Requested.ShouldBe(M(102m));
    }

    /// <summary>
    /// Sıfır tutarlı bacak ledger'a yazılmaz; bir kova tam olarak tükendiğinde
    /// sonrakinde yalnızca komisyon kalıyorsa o bacak yine de çıkıyor.
    /// </summary>
    [Fact]
    public void TutariSifirKalanBacak_YalnizcaKomisyonlaCikiyor()
    {
        var result = FundAllocator.ForTransfer(Wallet, Available(cash: 500m, card: 100m), M(100m), M(5m));

        result.Count.ShouldBe(2);

        result[0].FundType.ShouldBe(FundType.Card);
        result[0].Amount.ShouldBe(M(100m));
        result[0].Commission.ShouldBe(M(0m));

        result[1].FundType.ShouldBe(FundType.Cash);
        result[1].Amount.ShouldBe(M(0m));
        result[1].Commission.ShouldBe(M(5m));
    }

    /// <summary>
    /// Bacakların toplamı her zaman istenen toplama eşit. Dağıtımın asıl invariant'ı bu.
    /// </summary>
    [Theory]
    [InlineData(100, 0, 0, 100, 0)]
    [InlineData(500, 30, 0, 100, 5)]
    [InlineData(0, 250, 1000, 200, 12)]
    [InlineData(60, 45, 10, 100, 4)]
    public void BacaklarinToplami_IstenenToplamaEsit(
        decimal cash, decimal card, decimal promo, decimal amount, decimal commission)
    {
        var result = FundAllocator.ForTransfer(
            Wallet, Available(cash, card, promo), M(amount), M(commission));

        result.Sum(leg => leg.Amount.Amount).ShouldBe(amount);
        result.Sum(leg => leg.Commission.Amount).ShouldBe(commission);
    }

    // ------------------------------------------------------------------
    // Payment (decisions.md madde 37): promo tutarı karşılar, komisyonu değil.
    // Kullanılabilir promo alıcı işyerine göre çağıranda hesaplanıyor; cüzdanın
    // promo kovasının tamamı olmayabilir.
    // ------------------------------------------------------------------

    [Fact]
    public void Odeme_PromoYetiyorsa_TutarPromodan_KomisyonNakittenCikiyor()
    {
        var result = FundAllocator.ForPayment(
            Wallet, Available(cash: 500m, promo: 200m), usablePromo: M(200m), M(100m), M(2m));

        result.Count.ShouldBe(2);

        result[0].FundType.ShouldBe(FundType.Promo);
        result[0].Amount.ShouldBe(M(100m));
        result[0].Commission.ShouldBe(M(0m));

        result[1].FundType.ShouldBe(FundType.Cash);
        result[1].Amount.ShouldBe(M(0m));
        result[1].Commission.ShouldBe(M(2m));
    }

    [Fact]
    public void Odeme_PromoKismiYetiyorsa_KalanKartVeNakittenCikiyor()
    {
        var result = FundAllocator.ForPayment(
            Wallet, Available(cash: 500m, card: 30m, promo: 40m), usablePromo: M(40m), M(100m), M(0m));

        result.Select(leg => (leg.FundType, leg.Amount.Amount)).ShouldBe(
        [
            (FundType.Promo, 40m),
            (FundType.Card, 30m),
            (FundType.Cash, 30m)
        ]);
    }

    /// <summary>
    /// Cüzdanın promo kovası dolu ama bu işyerinde geçerli parti yok: dağıtım promo'ya
    /// hiç dokunmuyor.
    /// </summary>
    [Fact]
    public void Odeme_KullanilabilirPromoSifirsa_PromoyaDokunulmuyor()
    {
        var result = FundAllocator.ForPayment(
            Wallet, Available(cash: 500m, promo: 200m), usablePromo: M(0m), M(100m), M(0m));

        result.ShouldHaveSingleItem().FundType.ShouldBe(FundType.Cash);
    }

    [Fact]
    public void Odeme_KomisyonPromodanOdenmiyor()
    {
        var exception = Should.Throw<InsufficientFundsException>(
            () => FundAllocator.ForPayment(
                Wallet, Available(promo: 500m), usablePromo: M(500m), M(100m), M(2m)));

        exception.Available.ShouldBe(M(100m), "promo yalnızca tutar kadar sayılıyor");
        exception.Requested.ShouldBe(M(102m));
    }

    [Fact]
    public void Odeme_PromoVeNakitBirlikteYetmiyorsa_Reddediliyor()
    {
        var exception = Should.Throw<InsufficientFundsException>(
            () => FundAllocator.ForPayment(
                Wallet, Available(cash: 30m, promo: 50m), usablePromo: M(50m), M(100m), M(0m)));

        exception.Available.ShouldBe(M(80m));
        exception.Requested.ShouldBe(M(100m));
    }
}

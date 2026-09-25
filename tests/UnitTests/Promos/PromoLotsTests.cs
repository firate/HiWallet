using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.UnitTests.Promos;

/// <summary>
/// Ödemede promo partilerinin tüketim sırası (decisions.md madde 37). Sıra sabit:
/// bitişi en yakın olan önce (süresiz en sonda), sonra kısıtlı kapsam, sonra eski.
/// </summary>
public sealed class PromoLotsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static PromoLot Lot(
        decimal remaining, DateTimeOffset? expiresAt = null, bool restricted = true, int createdDay = 0) =>
        new(Guid.NewGuid(), remaining, expiresAt, restricted, T0.AddDays(createdDay));

    [Fact]
    public void BitisiEnYakinOlan_OnceTuketilir()
    {
        var later = Lot(50m, expiresAt: T0.AddDays(30));
        var sooner = Lot(50m, expiresAt: T0.AddDays(10));

        var takes = PromoLots.Take([later, sooner], 60m);

        takes.Count.ShouldBe(2);
        takes[0].ShouldBe(new PromoTake(sooner.GrantId, 50m));
        takes[1].ShouldBe(new PromoTake(later.GrantId, 10m));
    }

    [Fact]
    public void SuresizParti_EnSondaTuketilir()
    {
        var open = Lot(50m, expiresAt: null, createdDay: -10);
        var dated = Lot(50m, expiresAt: T0.AddDays(365));

        var takes = PromoLots.Take([open, dated], 20m);

        takes.ShouldHaveSingleItem().GrantId.ShouldBe(dated.GrantId);
    }

    [Fact]
    public void BitisEsitse_KisitliOlan_HerYerdeGecerlidenOnce()
    {
        var expiresAt = T0.AddDays(10);
        var everywhere = Lot(50m, expiresAt, restricted: false, createdDay: -10);
        var restricted = Lot(50m, expiresAt, restricted: true);

        var takes = PromoLots.Take([everywhere, restricted], 20m);

        takes.ShouldHaveSingleItem().GrantId.ShouldBe(restricted.GrantId);
    }

    [Fact]
    public void BitisVeKapsamEsitse_EskiOlanOnce()
    {
        var newer = Lot(50m, createdDay: 5);
        var older = Lot(50m, createdDay: 1);

        var takes = PromoLots.Take([newer, older], 20m);

        takes.ShouldHaveSingleItem().GrantId.ShouldBe(older.GrantId);
    }

    /// <summary>
    /// Partiler yetmezse hepsi tükenir ve eksik kalan çağıranın işi: ödemenin kalanı
    /// card ve cash kovalarından düşer.
    /// </summary>
    [Fact]
    public void PartilerYetmezse_HepsiTukenir()
    {
        var a = Lot(30m, expiresAt: T0.AddDays(1));
        var b = Lot(20m, expiresAt: T0.AddDays(2));

        var takes = PromoLots.Take([a, b], 100m);

        takes.Sum(t => t.Amount).ShouldBe(50m);
    }

    [Fact]
    public void KalaniSifirOlanParti_TakeUretmez()
    {
        var empty = Lot(0m, expiresAt: T0.AddDays(1));
        var full = Lot(40m, expiresAt: T0.AddDays(2));

        var takes = PromoLots.Take([empty, full], 10m);

        takes.ShouldHaveSingleItem().GrantId.ShouldBe(full.GrantId);
    }

    [Fact]
    public void UstSinirSifirsa_TakeUretmez()
    {
        PromoLots.Take([Lot(40m)], 0m).ShouldBeEmpty();
    }
}

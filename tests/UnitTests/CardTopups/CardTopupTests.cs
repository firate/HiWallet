using HiWallet.CardTopup.Domain;
using Topup = HiWallet.CardTopup.Domain.CardTopup;

namespace HiWallet.UnitTests.CardTopups;

/// <summary>
/// Kartla yüklemenin geçişleri. Zararsız tekrar <c>Ignored</c>, para kaybına işaret eden
/// çelişki <c>Conflict</c>; çelişkide durum değişmiyor (çekim saga'sının kuralı).
/// </summary>
public sealed class CardTopupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static Topup New(decimal amount = 250m) => Topup.Start(
        Guid.NewGuid(), "sub-1", "anahtar-1", Guid.NewGuid(), amount, "TRY", "stripe-fake",
        "http://web.test/kart-yukleme", Now.AddMinutes(30), Now);

    private static Topup Pending(decimal amount = 250m)
    {
        var topup = New(amount);
        topup.HoldPlaced(Guid.NewGuid(), Now);
        topup.PaymentOpened("pay_1", "http://stripe.test/odeme/pay_1", Now);
        return topup;
    }

    [Fact]
    public void Baslangic_Created()
    {
        var topup = New();

        topup.State.ShouldBe(CardTopupState.Created);
        topup.AccountId.ShouldBeNull();
        topup.PaymentUrl.ShouldBeNull();
    }

    [Fact]
    public void Baslangic_GecmisteBitenOturum_Hata()
    {
        Should.Throw<ArgumentException>(() => Topup.Start(
            Guid.NewGuid(), "sub", "k", Guid.NewGuid(), 10m, "TRY", "stripe-fake", "http://x", Now, Now));
    }

    [Fact]
    public void PayAlindi_Pending_TekrariZararsiz()
    {
        var topup = New();
        var account = Guid.NewGuid();

        topup.HoldPlaced(account, Now).ShouldBe(TransitionResult.Applied);
        topup.State.ShouldBe(CardTopupState.Pending);
        topup.AccountId.ShouldBe(account);

        topup.HoldPlaced(account, Now).ShouldBe(TransitionResult.Ignored);
        topup.HoldPlaced(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Conflict);
    }

    [Fact]
    public void PayVerilmedi_Rejected_SonDurum()
    {
        var topup = New();

        topup.HoldRejected("limit", Now).ShouldBe(TransitionResult.Applied);

        topup.State.ShouldBe(CardTopupState.Rejected);
        topup.IsTerminal.ShouldBeTrue();
        topup.FailureReason.ShouldBe("limit");
    }

    [Fact]
    public void OdemeAcildi_SayfaAdresiTutuluyor()
    {
        var topup = New();
        topup.HoldPlaced(Guid.NewGuid(), Now);

        topup.PaymentOpened("pay_1", "http://stripe.test/odeme/pay_1", Now).ShouldBe(TransitionResult.Applied);
        topup.PaymentUrl.ShouldBe("http://stripe.test/odeme/pay_1");

        topup.PaymentOpened("pay_1", "http://stripe.test/odeme/pay_1", Now).ShouldBe(TransitionResult.Ignored);
        topup.PaymentOpened("pay_2", "http://stripe.test/odeme/pay_2", Now).ShouldBe(TransitionResult.Conflict);
    }

    [Fact]
    public void PayAlinmadanOdemeAcilamaz()
    {
        New().PaymentOpened("pay_1", "http://x", Now).ShouldBe(TransitionResult.Conflict);
    }

    [Fact]
    public void Odendi_Paid_SayfaKapaniyor_TekrariZararsiz()
    {
        var topup = Pending();

        topup.Paid("pay_1", 250m, "TRY", Now).ShouldBe(TransitionResult.Applied);

        topup.State.ShouldBe(CardTopupState.Paid);
        topup.PaymentUrl.ShouldBeNull();
        topup.Paid("pay_1", 250m, "TRY", Now).ShouldBe(TransitionResult.Ignored);
    }

    /// <summary>Sağlayıcı başka bir tutar çektiyse durum değişmiyor; alarm.</summary>
    [Fact]
    public void Odendi_TutarFarkli_Celiski()
    {
        var topup = Pending();

        topup.Paid("pay_1", 300m, "TRY", Now).ShouldBe(TransitionResult.Conflict);
        topup.State.ShouldBe(CardTopupState.Pending);
    }

    [Fact]
    public void Odendi_BaskaOdeme_Celiski()
    {
        Pending().Paid("pay_baska", 250m, "TRY", Now).ShouldBe(TransitionResult.Conflict);
    }

    /// <summary>Pay serbest bırakıldıktan sonra gelen ödeme: para sağlayıcıda, limit onu saymıyor.</summary>
    [Fact]
    public void OdenmediDiyeKapanmisYukleme_Odenirse_Celiski()
    {
        var topup = Pending();
        topup.Failed("vazgeçildi", Now);

        topup.Paid("pay_1", 250m, "TRY", Now).ShouldBe(TransitionResult.Conflict);
        topup.State.ShouldBe(CardTopupState.Failed);
    }

    [Fact]
    public void Odenmedi_Failed_TekrariZararsiz()
    {
        var topup = Pending();

        topup.Failed("vazgeçildi", Now).ShouldBe(TransitionResult.Applied);

        topup.State.ShouldBe(CardTopupState.Failed);
        topup.FailureReason.ShouldBe("vazgeçildi");
        topup.Failed("süresi doldu", Now).ShouldBe(TransitionResult.Ignored);
        topup.FailureReason.ShouldBe("vazgeçildi");
    }

    /// <summary>
    /// Pay isteği cevapsız kaldıysa payın yazılıp yazılmadığı bilinmiyor; yükleme ödenmedi
    /// diye kapanabiliyor.
    /// </summary>
    [Fact]
    public void PayiAlinmamisYukleme_OdenmediyleKapanabilir()
    {
        New().Failed("başlatma yarıda kaldı", Now).ShouldBe(TransitionResult.Applied);
    }

    [Fact]
    public void OdenmisYukleme_OdenmediyleKapanamaz()
    {
        var topup = Pending();
        topup.Paid("pay_1", 250m, "TRY", Now);

        topup.Failed("vazgeçildi", Now).ShouldBe(TransitionResult.Conflict);
        topup.State.ShouldBe(CardTopupState.Paid);
    }

    [Fact]
    public void ReddedilmisYukleme_OdenmediyleKapanamaz()
    {
        var topup = New();
        topup.HoldRejected("limit", Now);

        topup.Failed("x", Now).ShouldBe(TransitionResult.Conflict);
    }
}

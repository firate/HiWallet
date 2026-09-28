using HiWallet.Onboarding.Domain;

namespace HiWallet.UnitTests.Onboarding;

/// <summary>
/// Kayıt: e-postaya giden kod, doğrulama ve tamamlama. Kod düz saklanmıyor; beş yanlış
/// denemeden sonra kayıt kilitleniyor ve kod on dakika geçerli.
/// </summary>
public sealed class RegistrationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    private static Registration Start(string code = "123456") =>
        Registration.Start(Guid.NewGuid(), "  Musteri@Ornek.COM ", code, T0);

    [Fact]
    public void Baslangic_EpostaKucukHarfVeKodDuzSaklanmaz()
    {
        var registration = Start();

        registration.Email.ShouldBe("musteri@ornek.com");
        registration.CodeHash.ShouldNotContain("123456");
        registration.EmailVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public void DogruKod_EpostaDogrulanir()
    {
        var registration = Start();

        registration.VerifyEmail("123456", T0.AddMinutes(1)).ShouldBe(CodeCheck.Verified);

        registration.EmailVerifiedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void YanlisKod_DogrulanmazVeDenemeSayilir()
    {
        var registration = Start();

        registration.VerifyEmail("000000", T0).ShouldBe(CodeCheck.WrongCode);

        registration.EmailVerifiedAt.ShouldBeNull();
        registration.FailedAttempts.ShouldBe(1);
    }

    /// <summary>Altı haneli kod bir milyon ihtimal; deneme sınırı olmasa kaba kuvvetle bulunurdu.</summary>
    [Fact]
    public void BesYanlisDeneme_DogruKoduDaKabulEtmez()
    {
        var registration = Start();

        for (var i = 0; i < Registration.MaxAttempts; i++)
        {
            registration.VerifyEmail("000000", T0);
        }

        registration.VerifyEmail("123456", T0).ShouldBe(CodeCheck.Locked);
        registration.EmailVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public void SuresiGecenKod_Reddedilir()
    {
        var registration = Start();

        registration.VerifyEmail("123456", T0 + Registration.CodeLifetime + TimeSpan.FromSeconds(1))
            .ShouldBe(CodeCheck.Expired);
    }

    [Fact]
    public void DogrulanmisKayit_TekrarDogrulanabilir()
    {
        var registration = Start();
        registration.VerifyEmail("123456", T0);

        registration.VerifyEmail("123456", T0.AddMinutes(1)).ShouldBe(CodeCheck.AlreadyVerified);
        registration.EmailVerifiedAt.ShouldBe(T0);
    }

    [Fact]
    public void DogrulanmamisKayit_Tamamlanamaz()
    {
        var registration = Start();

        Should.Throw<InvalidOperationException>(() => registration.AttachIdentity("kimlik-1"));
    }

    [Fact]
    public void Tamamlama_KimlikVeHesapYaziliyor()
    {
        var registration = Start();
        registration.VerifyEmail("123456", T0);

        registration.AttachIdentity("kimlik-1");
        registration.Complete(Guid.Parse("0a000000-0000-0000-0000-000000000001"), T0.AddMinutes(2));

        registration.Subject.ShouldBe("kimlik-1");
        registration.AccountId.ShouldBe(Guid.Parse("0a000000-0000-0000-0000-000000000001"));
        registration.CompletedAt.ShouldBe(T0.AddMinutes(2));
    }

    /// <summary>
    /// E-posta doğrulandıktan sonra kayıt sınırsız açık kalmıyor: doğrulanmış ama
    /// terk edilmiş bir kayıt, kimliği bulan başkasına parola belirletirdi.
    /// </summary>
    [Fact]
    public void DogrulamadanUzunSuraSonra_TamamlanamazSayilir()
    {
        var registration = Start();
        registration.VerifyEmail("123456", T0);

        registration.CanComplete(T0 + Registration.CompletionWindow).ShouldBeTrue();
        registration.CanComplete(T0 + Registration.CompletionWindow + TimeSpan.FromSeconds(1)).ShouldBeFalse();
    }
}

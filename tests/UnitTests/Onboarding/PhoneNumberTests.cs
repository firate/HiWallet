using HiWallet.Onboarding.Domain;

namespace HiWallet.UnitTests.Onboarding;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("05321234567")]
    [InlineData("5321234567")]
    [InlineData("+905321234567")]
    [InlineData("905321234567")]
    [InlineData("0532 123 45 67")]
    [InlineData("(0532) 123-45-67")]
    public void TurkiyeCepNumarasi_TekBicimeIner(string value)
    {
        PhoneNumber.TryParse(value, out var phone).ShouldBeTrue();
        phone.Value.ShouldBe("+905321234567");
    }

    [Theory]
    [InlineData("02121234567")] // sabit hat: SMS gitmiyor
    [InlineData("053212345")] // eksik hane
    [InlineData("+15551234567")] // yurt dışı
    [InlineData("abc")]
    [InlineData("")]
    public void CepNumarasiOlmayan_Reddedilir(string value)
    {
        PhoneNumber.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Maskeli_SonIkiHaneAcik()
    {
        PhoneNumber.TryParse("05321234567", out var phone).ShouldBeTrue();

        phone.Masked.ShouldBe("+90 532 *** ** 67");
    }
}

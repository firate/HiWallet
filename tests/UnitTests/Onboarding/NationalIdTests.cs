using HiWallet.Onboarding.Domain;

namespace HiWallet.UnitTests.Onboarding;

public sealed class NationalIdTests
{
    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    public void GecerliNumara_Kabul(string value)
    {
        NationalId.TryParse(value, out var id).ShouldBeTrue();
        id.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("12345678951")] // son hane tutmuyor
    [InlineData("12345678940")] // onuncu hane tutmuyor
    [InlineData("01234567890")] // sıfırla başlıyor
    [InlineData("1234567895")] // on hane
    [InlineData("1234567895a")]
    [InlineData("")]
    public void GecersizNumara_Reddedilir(string value)
    {
        NationalId.TryParse(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void Bosluklar_Temizlenir()
    {
        NationalId.TryParse(" 100 000 001 46 ", out var id).ShouldBeTrue();
        id.Value.ShouldBe("10000000146");
    }

    /// <summary>Numara ekranda ve logda açık görünmüyor.</summary>
    [Fact]
    public void Maskeli_IlkVeSonIkiHane()
    {
        NationalId.TryParse("10000000146", out var id).ShouldBeTrue();

        id.Masked.ShouldBe("10*******46");
        id.ToString().ShouldBe(id.Masked);
    }
}

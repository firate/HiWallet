using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.UnitTests.Withdrawals;

/// <summary>
/// IBAN doğrulaması bu projede sıradan bir input validation DEĞİL: komisyonun
/// koşulsuz iade edilmesi kuralı buna dayanıyor (overview.md madde 6). Gerekçe şu —
/// başarısızlığın sebebi ya bizde ya bankada; müşteri kaynaklı tek gerçekçi senaryo
/// yanlış IBAN ve o da saga başlamadan burada eleniyor. Bu kontrol zayıflarsa
/// "komisyon koşulsuz iade edilir" kuralı yalan olur.
/// </summary>
public sealed class IbanTests
{
    // Gerçek biçimde, mod-97'si tutan örnekler.
    [Theory]
    [InlineData("TR330006100519786457841326")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("DE89370400440532013000")]
    [InlineData("FR1420041010050500013M02606")]
    public void GecerliIban_KabulEdilir(string value)
    {
        Iban.TryFrom(value, out var iban).ShouldBeTrue();
        iban.Value.ShouldBe(value);
    }

    [Fact]
    public void BosluklarVeKucukHarf_Normalize_Edilir()
    {
        // Banka arayüzleri IBAN'ı gruplayarak gösteriyor, kullanıcı da öyle
        // yapıştırıyor. Reddetmek yerine normalize etmek doğru davranış.
        Iban.TryFrom("tr33 0006 1005 1978 6457 8413 26", out var iban).ShouldBeTrue();

        iban.Value.ShouldBe("TR330006100519786457841326");
    }

    [Fact]
    public void TekKarakterDegisirse_Reddedilir()
    {
        // Mod-97'nin bütün amacı bu: yazım hatası yakalanmalı.
        Iban.TryFrom("TR330006100519786457841327", out _).ShouldBeFalse();
    }

    [Fact]
    public void IkiKarakterYerDegistirirse_Reddedilir()
    {
        // Transpozisyon en sık yapılan hata ve basit bir toplam kontrolü bunu
        // yakalayamaz; mod-97 yakalıyor.
        Iban.TryFrom("TR330006100519786457841362", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Çok kısa.
    [InlineData("TR33")]
    // Çok uzun (34 üstü).
    [InlineData("TR3300061005197864578413260000000000")]
    // Ülke kodu harf değil.
    [InlineData("12330006100519786457841326")]
    // Kontrol basamağı rakam değil.
    [InlineData("TRAB0006100519786457841326")]
    // İçinde alfanümerik olmayan karakter.
    [InlineData("TR33-0006-1005-1978-6457-8413-26")]
    public void BozukGirdi_IstisnaFirlatmadanReddedilir(string? value)
    {
        // Girdi dış dünyadan geliyor; hiçbir biçim bozukluğu istisnaya dönüşmemeli.
        // Dönerse sınırda 400 yerine 500 alırdık.
        Iban.TryFrom(value, out _).ShouldBeFalse();
    }

    [Fact]
    public void KontrolBasamagi_00_01_99_Reddedilir()
    {
        // Standart bu üç değeri geçersiz sayıyor. Mod-97 hesabı 01'i zaten
        // eleyemez — ayrıca kontrol edilmesi gerekiyor.
        Iban.TryFrom("TR000006100519786457841326", out _).ShouldBeFalse();
        Iban.TryFrom("TR010006100519786457841326", out _).ShouldBeFalse();
        Iban.TryFrom("TR990006100519786457841326", out _).ShouldBeFalse();
    }

    [Fact]
    public void From_GecersizdeIstisnaFirlatir()
    {
        Should.Throw<ArgumentException>(() => Iban.From("TR330006100519786457841327"));
    }

    [Fact]
    public void Default_KullanilamazVeSoylemesiAcikOlur()
    {
        // Currency ile aynı yaklaşım: default(Iban) sessizce boş bir değer
        // taşımamalı, kullanıldığı yerde patlamalı.
        var uninitialized = default(Iban);

        Should.Throw<InvalidOperationException>(() => uninitialized.Value);
    }

    [Fact]
    public void Maskeli_Hali_SonDortHaneyiBirakir()
    {
        // Log ve hata mesajlarında tam IBAN dolaşmamalı. Son dört hane müşteriye
        // "hangi hesap" sorusunu cevaplatmaya yetiyor.
        const string value = "TR330006100519786457841326";
        var iban = Iban.From(value);

        iban.Masked.ShouldBe("TR33" + new string('*', value.Length - 8) + "1326");
        iban.Masked.Length.ShouldBe(value.Length);
        iban.Masked.ShouldNotContain("6457");
    }
}

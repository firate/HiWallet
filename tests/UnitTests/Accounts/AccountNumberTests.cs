using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.UnitTests.Accounts;

/// <summary>
/// Hesap numarası insanın yazdığı ve telefonda söylediği şey; kontrol hanesi yanlış
/// yazılan numarayı sınırda yakalıyor, para başkasının hesabına gitmeden.
/// </summary>
public sealed class AccountNumberTests
{
    [Fact]
    public void Yeni_OnHane_IlkHaneSifirDegil_KontrolHanesiTutuyor()
    {
        for (var i = 0; i < 1000; i++)
        {
            var number = AccountNumber.New();

            number.Value.Length.ShouldBe(AccountNumber.Length);
            number.Value.ShouldAllBe(c => char.IsAsciiDigit(c));
            number.Value[0].ShouldNotBe('0');
            AccountNumber.TryFrom(number.Value, out _).ShouldBeTrue();
        }
    }

    /// <summary>Sıralı değil: art arda iki numaradan sonrakini tahmin etmek mümkün olmamalı.</summary>
    [Fact]
    public void Yeni_Rastgele() =>
        Enumerable.Range(0, 100).Select(_ => AccountNumber.New()).Distinct().Count().ShouldBe(100);

    [Theory]
    [InlineData("1234567897")]
    [InlineData("9000000001")]
    public void GecerliNumara_KabulEdilir(string value)
    {
        AccountNumber.TryFrom(value, out var number).ShouldBeTrue();
        number.Value.ShouldBe(value);
    }

    /// <summary>Uygulamalar numarayı gruplayarak gösteriyor; kullanıcı da öyle yapıştırıyor.</summary>
    [Theory]
    [InlineData("123 456 7897")]
    [InlineData(" 1234567897 ")]
    public void Bosluklar_Atilir(string value)
    {
        AccountNumber.TryFrom(value, out var number).ShouldBeTrue();
        number.Value.ShouldBe("1234567897");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890")] // kontrol hanesi tutmuyor
    [InlineData("123456789")] // dokuz hane
    [InlineData("12345678970")] // on bir hane
    [InlineData("0234567899")] // kontrol hanesi tutuyor ama ilk hane sıfır
    [InlineData("12345678a7")]
    [InlineData("１２３４５６７８９７")] // tam genişlikli rakamlar ASCII değil
    public void GecersizNumara_Reddedilir(string? value) =>
        AccountNumber.TryFrom(value, out _).ShouldBeFalse();

    [Fact]
    public void From_GecersizNumara_HamGirdiyiMesajaYazmaz()
    {
        var error = Should.Throw<ArgumentException>(() => AccountNumber.From("1234567890"));

        error.Message.ShouldNotContain("1234567890");
    }

    /// <summary>Tek bir hanesi yanlış yazılan her numara reddediliyor.</summary>
    [Fact]
    public void TekHaneHatasi_Yakalanir()
    {
        const string valid = "1234567897";

        for (var position = 0; position < valid.Length; position++)
        {
            foreach (var digit in "0123456789".Where(d => d != valid[position]))
            {
                var typo = string.Concat(valid[..position], digit.ToString(), valid[(position + 1)..]);

                AccountNumber.TryFrom(typo, out _).ShouldBeFalse(typo);
            }
        }
    }

    /// <summary>
    /// Yan yana iki hanenin yer değiştirmesi yakalanıyor. Luhn'un bilinen tek boşluğu
    /// 09 ile 90: ikisinin kontrol toplamı aynı.
    /// </summary>
    [Fact]
    public void YanYanaHanelerinYerDegistirmesi_Yakalanir()
    {
        for (var i = 0; i < 200; i++)
        {
            var valid = AccountNumber.New().Value;

            for (var position = 0; position < valid.Length - 1; position++)
            {
                var (a, b) = (valid[position], valid[position + 1]);

                if (a == b || (a, b) is ('0', '9') or ('9', '0'))
                {
                    continue;
                }

                var swapped = string.Concat(valid[..position], b.ToString(), a.ToString(), valid[(position + 2)..]);

                AccountNumber.TryFrom(swapped, out _).ShouldBeFalse(swapped);
            }
        }
    }

    /// <summary>
    /// Havale açıklaması serbest metin: numara bitişik ya da uygulamanın gösterdiği gibi
    /// 3-3-4 gruplanmış yazılıyor, önünde arkasında başka sözcükler oluyor.
    /// </summary>
    [Theory]
    [InlineData("4817305925")]
    [InlineData("Hesap no: 481 730 5925 Ayşe")]
    [InlineData("481-730-5925")]
    [InlineData("HW4817305925")]
    [InlineData("4817305925 1000 TL yükleme")]
    [InlineData("yükleme\n4817305925")]
    public void Metinde_NumaraBulunur(string text)
    {
        AccountNumber.FindIn(text).ShouldBe([AccountNumber.From("4817305925")]);
    }

    /// <summary>
    /// Bulunmayanlar: kontrol hanesi tutmayan, daha uzun bir rakam dizisinin parçası
    /// (TCKN, IBAN) ve dört haneli gruplar halindeki IBAN.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kira")]
    [InlineData("4817305924")]
    [InlineData("14817305925")]
    [InlineData("48173059251")]
    [InlineData("TR33 0006 1005 1978 6457 8413 26")]
    [InlineData("TR330006100519786457841326")]
    public void Metinde_NumaraYok(string? text)
    {
        AccountNumber.FindIn(text).ShouldBeEmpty();
    }

    /// <summary>Aynı numara iki kez yazılmışsa tek; farklı iki numara ikisi de dönüyor, seçilmiyor.</summary>
    [Fact]
    public void Metinde_TekrarlarBirKezSayilir_FarkliNumaralarAyriDoner()
    {
        AccountNumber.FindIn("4817305925 / 481 730 5925").Count.ShouldBe(1);
        AccountNumber.FindIn("4817305925 ve 1234567897").Count.ShouldBe(2);
    }
}

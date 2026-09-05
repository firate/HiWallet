using System.Text;
using HiWallet.TopupWebhook.Application;

namespace HiWallet.UnitTests.Topups;

public sealed class WebhookSignatureTests
{
    private const string Secret = "paylasilan-sir";

    private static readonly byte[] Body =
        Encoding.UTF8.GetBytes("""{"eventId":"evt_1","amount":100.00}""");

    [Fact]
    public void UretilenImza_KendiDogrulamasindanGecer()
    {
        var signature = WebhookSignature.Compute(Body, Secret);

        WebhookSignature.IsValid(Body, Secret, signature).ShouldBeTrue();
    }

    [Fact]
    public void ImzaFormati_sha256OnEkiVeKucukHarfHex()
    {
        var signature = WebhookSignature.Compute(Body, Secret);

        signature.ShouldStartWith("sha256=");
        signature["sha256=".Length..].Length.ShouldBe(64);
        signature.ShouldBe(signature.ToLowerInvariant());
    }

    [Fact]
    public void GovdeDegisirse_ImzaTutmaz()
    {
        var signature = WebhookSignature.Compute(Body, Secret);

        // Tek karakter: tutar 100 yerine 900. İmzanın asıl işi bu.
        var tampered = Encoding.UTF8.GetBytes("""{"eventId":"evt_1","amount":900.00}""");

        WebhookSignature.IsValid(tampered, Secret, signature).ShouldBeFalse();
    }

    [Fact]
    public void BaskaSecretIleUretilmisImza_Reddedilir()
    {
        var signature = WebhookSignature.Compute(Body, "baska-sir");

        WebhookSignature.IsValid(Body, Secret, signature).ShouldBeFalse();
    }

    [Fact]
    public void BosGovde_YineDeTutarliImzaUretir()
    {
        // Sağlayıcı boş gövde göndermez ama imza fonksiyonu bunda patlamamalı;
        // patlarsa controller'da 500'e dönerdi, oysa doğru cevap 401.
        var signature = WebhookSignature.Compute([], Secret);

        WebhookSignature.IsValid([], Secret, signature).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // Ön ek yok.
    [InlineData("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20")]
    // Yanlış algoritma adı.
    [InlineData("sha512=0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20")]
    // Kısa hex.
    [InlineData("sha256=0102")]
    // Uzun hex.
    [InlineData("sha256=0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f2021")]
    // Hex olmayan karakter.
    [InlineData("sha256=zzzz030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20")]
    // Sadece ön ek.
    [InlineData("sha256=")]
    public void BozukBaslik_IstisnaFirlatmadanReddedilir(string? header)
    {
        // Başlık tamamen dış dünyadan geliyor; hiçbir biçim bozukluğu istisnaya
        // dönüşmemeli. Dönerse 401 yerine 500 alırdık ve bu bir DoS düğmesi olurdu.
        WebhookSignature.IsValid(Body, Secret, header).ShouldBeFalse();
    }

    [Fact]
    public void BuyukHarfHex_KabulEdilir()
    {
        // Karşılaştırma metin üzerinde değil BAYT üzerinde: hex çözülüp
        // FixedTimeEquals ile bakılıyor. Dolayısıyla hex'in harf büyüklüğü fark
        // etmiyor. Metin karşılaştırması yapılsaydı hem bu senaryo kırılırdı hem
        // de karşılaştırma sabit zamanlı olmazdı.
        var signature = "sha256=" + WebhookSignature.Compute(Body, Secret)["sha256=".Length..]
            .ToUpperInvariant();

        WebhookSignature.IsValid(Body, Secret, signature).ShouldBeTrue();
    }
}

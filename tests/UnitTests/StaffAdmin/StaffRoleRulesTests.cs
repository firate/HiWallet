using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.UnitTests.StaffAdmin;

public sealed class StaffRoleRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Operasyon")]
    [InlineData("Çağrı merkezi")]
    [InlineData("Destek_2")]
    [InlineData("Finans-müdürü")]
    public void Ad_Gecerli(string name) => StaffRoleRules.IsWellFormed(name).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData(" Boşlukla")]
    [InlineData("Sonda boşluk ")]
    [InlineData("-Tireyle")]
    [InlineData("customer.view")]
    [InlineData("rol/alt")]
    public void Ad_Gecersiz(string name) => StaffRoleRules.IsWellFormed(name).ShouldBeFalse();

    [Fact]
    public void Ad_Uzun_Gecersiz() =>
        StaffRoleRules.IsWellFormed(new string('a', StaffRoleRules.MaxNameLength + 1)).ShouldBeFalse();

    /// <summary>Kayıt ve panel izinleri hep aynı sırada görüyor: seçilme sırası değil, kodun sırası.</summary>
    [Fact]
    public void Rol_IzinleriTekrarsizVeKodunSirasiyla()
    {
        var role = StaffRole.Create(
            "Operasyon", null,
            [StaffPermissions.WithdrawalReview, StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview],
            Now);

        role.Permissions.ShouldBe([StaffPermissions.CustomerView, StaffPermissions.WithdrawalReview]);

        role.Change("Ekip", [StaffPermissions.StaffManage, StaffPermissions.CustomerView]);

        role.Permissions.ShouldBe([StaffPermissions.CustomerView, StaffPermissions.StaffManage]);
    }

    [Fact]
    public void Rol_AdBuyukKucukHarfFarkiylaAyni() =>
        StaffRole.NormalizeName("operasyon").ShouldBe(StaffRole.NormalizeName("OPERASYON"));

    [Fact]
    public void Calisan_EpostaKucukHarfle() =>
        StaffMember.Invite(Guid.NewGuid(), " Ayse@Ornek.COM ", null, null, Now).Email.ShouldBe("ayse@ornek.com");

    /// <summary>İlk görüldüğü an davetin tamamlandığı an; sonraki girişler onu değiştirmiyor.</summary>
    [Fact]
    public void Calisan_IlkGorulmeBirKezYaziliyor()
    {
        var member = StaffMember.Invite(Guid.NewGuid(), "a@ornek.com", null, null, Now);

        member.MarkSeen(Now.AddMinutes(5)).ShouldBeTrue();
        member.MarkSeen(Now.AddMinutes(9)).ShouldBeFalse();

        member.ActivatedAt.ShouldBe(Now.AddMinutes(5));
    }
}

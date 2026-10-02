using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.UnitTests.StaffAdmin;

public sealed class StaffRoleRulesTests
{
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

    /// <summary>Token'da rol ile izin ve kimlik sağlayıcının rolleri ayırt edilebilmeli.</summary>
    [Theory]
    [InlineData("offline_access")]
    [InlineData("UMA_AUTHORIZATION")]
    [InlineData("default-roles-hiwallet-staff")]
    [InlineData(StaffPermissions.StaffManage)]
    public void Ad_Ayrilmis(string name) => StaffRoleRules.IsReserved(name).ShouldBeTrue();

    [Fact]
    public void Ad_Ayrilmamis() => StaffRoleRules.IsReserved("Operasyon").ShouldBeFalse();
}

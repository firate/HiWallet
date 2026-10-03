using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;

namespace HiWallet.UnitTests.Accounts;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bireysel_KimliginHesabiOlarakUnknownSeviyedeAcilir()
    {
        var account = Account.OpenPerson(Guid.NewGuid(), AccountNumber.New(), "kimlik-1", Now);

        account.Type.ShouldBe(AccountType.Person);
        account.Holder.ShouldBe("kimlik-1");
        account.KycLevel.ShouldBe(KycLevel.Unknown);
    }

    [Fact]
    public void Bireysel_KimliksizAcilmaz()
    {
        Should.Throw<ArgumentException>(() => Account.OpenPerson(Guid.NewGuid(), AccountNumber.New(), " ", Now));
    }

    [Fact]
    public void Isyeri_SeviyesizVeSahipsizAcilir()
    {
        var account = Account.OpenBusiness(Guid.NewGuid(), AccountNumber.New(), Now);

        account.Type.ShouldBe(AccountType.Business);
        account.Holder.ShouldBeNull();
        account.KycLevel.ShouldBeNull();
    }

    [Fact]
    public void Bireysel_SeviyesiYukselir()
    {
        var account = Account.OpenPerson(Guid.NewGuid(), AccountNumber.New(), "kimlik-2", Now);

        account.RaiseKycLevel(KycLevel.Unverified).ShouldBeTrue();

        account.KycLevel.ShouldBe(KycLevel.Unverified);
    }

    /// <summary>
    /// Seviyeyi yükselten yollar birbirinden habersiz (kayıt, banka girişi, backoffice);
    /// geç gelen bir alt seviye, ulaşılmış bir üst seviyeyi geri almamalı.
    /// </summary>
    [Fact]
    public void DahaDusukSeviye_Degistirmez()
    {
        var account = Account.OpenPerson(Guid.NewGuid(), AccountNumber.New(), "kimlik-3", Now);
        account.RaiseKycLevel(KycLevel.Verified);

        account.RaiseKycLevel(KycLevel.Unverified).ShouldBeFalse();

        account.KycLevel.ShouldBe(KycLevel.Verified);
    }

    /// <summary>İşyerinin doğrulaması ayrı bir iş; müşteri seviyeleri ona uygulanmıyor.</summary>
    [Fact]
    public void Isyeri_SeviyeAlamaz()
    {
        var account = Account.OpenBusiness(Guid.NewGuid(), AccountNumber.New(), Now);

        Should.Throw<KycLevelNotApplicableException>(() => account.RaiseKycLevel(KycLevel.Contracted));
        account.KycLevel.ShouldBeNull();
    }
}

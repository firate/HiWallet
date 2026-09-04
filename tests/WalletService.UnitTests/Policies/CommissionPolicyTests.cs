using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.UnitTests.Policies;

public sealed class CommissionPolicyTests
{
    private static readonly Currency Try = Currency.From("TRY");

    private static CommissionPolicy Policy(params (TransferType Type, CommissionRate Rate)[] rates)
    {
        return new CommissionPolicy(rates.ToDictionary(r => r.Type, r => r.Rate));
    }

    [Fact]
    public void Calculate_TarifeYok_SifirDoner()
    {
        var policy = Policy();

        policy.Calculate(TransferType.P2P, new Money(100m, Try)).ShouldBe(Money.Zero(Try));
    }

    [Fact]
    public void Calculate_DuzOran_Keser()
    {
        var policy = Policy((TransferType.Payment, new CommissionRate(0.02m)));

        policy.Calculate(TransferType.Payment, new Money(100m, Try)).ShouldBe(new Money(2m, Try));
    }

    [Fact]
    public void Calculate_MinorUnitAltinaDusenSonuc_KurusaYuvarlanir()
    {
        // 33,33 × %2,9 = 0,966570. Dört haneye yuvarlansaydı cüzdana 0,9666 yazılır ve
        // müşterinin çekemeyeceği bir bakiye oluşurdu — banka 2 haneden fazlasını kabul
        // etmiyor (decisions.md madde 19).
        var policy = Policy((TransferType.Payment, new CommissionRate(0.029m)));

        var commission = policy.Calculate(TransferType.Payment, new Money(33.33m, Try));

        commission.ShouldBe(new Money(0.97m, Try));
    }

    [Fact]
    public void Calculate_TamYarim_SifirdanUzagaYuvarlanir()
    {
        // 25,00 × %2 = 0,50 değil; 12,50 × %2 = 0,25. Yarım noktası için 0,125 üreten
        // bir oran seçiliyor: banker's rounding 0,12 verirdi, AwayFromZero 0,13
        // (decisions.md madde 19).
        var policy = Policy((TransferType.Payment, new CommissionRate(0.01m)));

        policy.Calculate(TransferType.Payment, new Money(12.50m, Try))
            .ShouldBe(new Money(0.13m, Try));
    }

    [Fact]
    public void Calculate_OranMinimumAltinda_MinimumUygulanir()
    {
        var policy = Policy((TransferType.Payment, new CommissionRate(0.02m, Minimum: 5m)));

        policy.Calculate(TransferType.Payment, new Money(10m, Try)).ShouldBe(new Money(5m, Try));
    }

    [Fact]
    public void Calculate_OranTavaniAsiyor_TavanUygulanir()
    {
        var policy = Policy((TransferType.Payment, new CommissionRate(0.02m, Maximum: 50m)));

        policy.Calculate(TransferType.Payment, new Money(10_000m, Try)).ShouldBe(new Money(50m, Try));
    }

    [Fact]
    public void Calculate_SonucHerZamanPozitifVeyaSifir()
    {
        // İşaret ledger'a yazılırken veriliyor, policy vermez.
        var policy = Policy((TransferType.Payment, new CommissionRate(0.02m)));

        policy.Calculate(TransferType.Payment, new Money(100m, Try)).IsDebit.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_PozitifOlmayanTutar_Reddeder(int amount)
    {
        var policy = Policy((TransferType.Payment, new CommissionRate(0.02m)));

        Should.Throw<ArgumentException>(() => policy.Calculate(TransferType.Payment, new Money(amount, Try)));
    }
}

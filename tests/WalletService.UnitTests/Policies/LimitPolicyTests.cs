using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.UnitTests.Policies;

public sealed class LimitPolicyTests
{
    private static readonly Currency Try = Currency.From("TRY");
    private static readonly Guid AccountId = Guid.Parse("0a000000-0000-0000-0000-000000000001");

    private static LimitPolicy Policy(params (TransferType Type, TransferLimit Limit)[] limits)
    {
        return new LimitPolicy(limits.ToDictionary(l => l.Type, l => l.Limit));
    }

    [Fact]
    public void Ensure_TarifeYok_Gecer()
    {
        var policy = Policy();

        Should.NotThrow(() => policy.Ensure(
            AccountId, TransferType.P2P, new Money(1_000_000m, Try), Money.Zero(Try)));
    }

    [Fact]
    public void Ensure_IslemTavaniAsiliyor_Patlar()
    {
        var policy = Policy((TransferType.P2P, new TransferLimit(PerTransaction: 1_000m)));

        Should.Throw<LimitExceededException>(() => policy.Ensure(
                AccountId, TransferType.P2P, new Money(1_000.01m, Try), Money.Zero(Try)))
            .LimitName.ShouldBe("P2P.PerTransaction");
    }

    [Fact]
    public void Ensure_IslemTavaniTamSinirda_Gecer()
    {
        var policy = Policy((TransferType.P2P, new TransferLimit(PerTransaction: 1_000m)));

        Should.NotThrow(() => policy.Ensure(
            AccountId, TransferType.P2P, new Money(1_000m, Try), Money.Zero(Try)));
    }

    [Fact]
    public void Ensure_GunlukToplamAsiliyor_Patlar()
    {
        var policy = Policy((TransferType.P2P, new TransferLimit(Daily: 10_000m)));

        Should.Throw<LimitExceededException>(() => policy.Ensure(
                AccountId, TransferType.P2P, new Money(1m, Try), new Money(10_000m, Try)))
            .LimitName.ShouldBe("P2P.Daily");
    }

    [Fact]
    public void Ensure_GunlukLimitTekIslemleAsilmiyorAmaBirikimle_Patlar()
    {
        var policy = Policy((TransferType.P2P, new TransferLimit(Daily: 10_000m)));

        Should.Throw<LimitExceededException>(() => policy.Ensure(
            AccountId, TransferType.P2P, new Money(3_000m, Try), new Money(8_000m, Try)));
    }

    [Fact]
    public void Ensure_Asim_HesapKimligiTasir()
    {
        // Kapsam cüzdan değil hesap: bir hesabın aynı currency'de birden fazla cüzdanı
        // olabildiği için cüzdan bazında limit ikinci cüzdan açılarak aşılırdı
        // (decisions.md madde 20).
        var policy = Policy((TransferType.P2P, new TransferLimit(Daily: 100m)));

        var ex = Should.Throw<LimitExceededException>(() => policy.Ensure(
            AccountId, TransferType.P2P, new Money(101m, Try), Money.Zero(Try)));

        ex.AccountId.ShouldBe(AccountId);
    }

    [Fact]
    public void Ensure_LimitAsimi_IsKuraliReddidir()
    {
        // 422 dönebilmesi için DomainException olmalı; concurrency çakışması (409) ile
        // karıştırılmıyor (CLAUDE.md "API").
        typeof(LimitExceededException).IsAssignableTo(typeof(DomainException)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(TransferType.P2P, AccountType.Person, AccountType.Person, true)]
    [InlineData(TransferType.P2P, AccountType.Person, AccountType.Business, false)]
    [InlineData(TransferType.P2B, AccountType.Person, AccountType.Business, true)]
    [InlineData(TransferType.B2P, AccountType.Business, AccountType.Person, true)]
    [InlineData(TransferType.B2B, AccountType.Business, AccountType.Business, true)]
    [InlineData(TransferType.Payment, AccountType.Person, AccountType.Business, true)]
    [InlineData(TransferType.Payment, AccountType.Business, AccountType.Business, true)]
    [InlineData(TransferType.Payment, AccountType.Person, AccountType.Person, false)]
    public void Matches_TipTaraflarlaTutarliMi(
        TransferType type, AccountType sender, AccountType receiver, bool expected)
    {
        type.Matches(sender, receiver).ShouldBe(expected);
    }
}

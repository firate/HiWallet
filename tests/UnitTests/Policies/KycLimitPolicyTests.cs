using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.UnitTests.Policies;

public sealed class KycLimitPolicyTests
{
    private static readonly Currency Try = Currency.From("TRY");
    private static readonly Guid AccountId = Guid.Parse("0a000000-0000-0000-0000-000000000002");

    private static readonly KycLimitPolicy Policy = new(
        new Dictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>>
        {
            [KycLevel.Unknown] = new Dictionary<KycMovement, decimal>
            {
                [KycMovement.OutgoingTransfer] = 0m,
                [KycMovement.IncomingTransfer] = 0m,
                [KycMovement.Deposit] = 0m,
                [KycMovement.IncomingTotal] = 0m
            },
            [KycLevel.Unverified] = new Dictionary<KycMovement, decimal>
            {
                [KycMovement.OutgoingTransfer] = 0m,
                [KycMovement.IncomingTransfer] = 5_500m,
                [KycMovement.Deposit] = 5_500m,
                [KycMovement.IncomingTotal] = 5_500m
            },
            [KycLevel.Contracted] = new Dictionary<KycMovement, decimal>
            {
                [KycMovement.IncomingTransfer] = 1_000_000m,
                [KycMovement.Deposit] = 1_000_000m,
                [KycMovement.IncomingTotal] = 2_000_000m
            }
        },
        new Dictionary<KycLevel, decimal>
        {
            [KycLevel.Unknown] = 0m,
            [KycLevel.Unverified] = 5_500m
        });

    private static Money Try_(decimal amount) => new(amount, Try);

    private static IncomingThisMonth Received(decimal deposits = 0m, decimal transfers = 0m) =>
        new(Try_(deposits), Try_(transfers));

    [Fact]
    public void Unknown_HerHareketKapali()
    {
        Should.Throw<LimitExceededException>(() => Policy.EnsureOutgoing(
                AccountId, KycLevel.Unknown, KycMovement.OutgoingTransfer, Try_(0.01m), Try_(0m)))
            .LimitName.ShouldBe("Kyc.OutgoingTransfer.Monthly");
    }

    [Fact]
    public void AylikToplamSinirda_Gecer()
    {
        Should.NotThrow(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.IncomingTransfer, Try_(750m), Received(transfers: 4_750m), Try_(0m)));
    }

    /// <summary>
    /// Alıcının limiti aşıldığında gönderene dönen hata alıcının hesabını söylemiyor:
    /// gönderen yalnızca cüzdanı biliyor, arkasındaki hesap onun bilgisi değil.
    /// </summary>
    [Fact]
    public void GelenAylikToplamAsiliyor_AliciyiSoylemedenReddedilir()
    {
        var error = Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.IncomingTransfer, Try_(750.01m), Received(transfers: 4_750m), Try_(0m)));

        error.LimitName.ShouldBe("Kyc.IncomingTransfer.Monthly");
        error.Message.ShouldNotContain(AccountId.ToString());
    }

    /// <summary>
    /// Yükleme ve gelen transfer ayın TOPLAM girişini paylaşıyor: ikisi ayrı ayrı
    /// sınırlansaydı ay içinde harcanan para tavanın iki katını hesaba sokardı.
    /// </summary>
    [Fact]
    public void YuklemeVeGelenTransfer_AyinToplamGirisiniPaylasir()
    {
        var transfer = Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.IncomingTransfer, Try_(600m), Received(deposits: 5_000m), Try_(0m)));
        var deposit = Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.Deposit, Try_(600m), Received(transfers: 5_000m), Try_(0m)));

        transfer.LimitName.ShouldBe("Kyc.IncomingTotal.Monthly");
        deposit.LimitName.ShouldBe("Kyc.IncomingTotal.Monthly");
    }

    [Fact]
    public void YuklemeninKendiAylikLimiti_AyricaSayilir()
    {
        Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
                KycLevel.Unverified, KycMovement.Deposit, Try_(600m), Received(deposits: 5_000m), Try_(0m)))
            .LimitName.ShouldBe("Kyc.Deposit.Monthly");
    }

    /// <summary>Bakiye tavanı ayın toplamından bağımsız: harcanmamış para da sayılıyor.</summary>
    [Fact]
    public void BakiyeTavaniAsiliyor_Reddedilir()
    {
        var error = Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.Deposit, Try_(600m), Received(), Try_(5_000m)));

        error.LimitName.ShouldBe("Kyc.Balance");
    }

    [Fact]
    public void BakiyeTavaniSinirda_Gecer()
    {
        Should.NotThrow(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.Deposit, Try_(500m), Received(), Try_(5_000m)));
    }

    /// <summary>Kimliği tespit edilmiş seviyede bakiye tavanı yok; aylık limitler geçerli.</summary>
    [Fact]
    public void KimligiTespitEdilmis_BakiyeTavaniYok()
    {
        Should.NotThrow(() => Policy.EnsureIncoming(
            KycLevel.Contracted, KycMovement.IncomingTransfer, Try_(10_000m), Received(), Try_(900_000m)));
    }

    /// <summary>Tarifede olmayan seviye ya da hareket kapalı sayılıyor, açık değil.</summary>
    [Fact]
    public void TanimsizHareket_Kapali()
    {
        Should.Throw<LimitExceededException>(() => Policy.EnsureOutgoing(
            AccountId, KycLevel.Contracted, KycMovement.Payment, Try_(1m), Try_(0m)));
    }

    [Fact]
    public void GidenHareketGelenGibiSorulamaz()
    {
        Should.Throw<ArgumentException>(() => Policy.EnsureIncoming(
            KycLevel.Contracted, KycMovement.Payment, Try_(1m), Received(), Try_(0m)));
    }

    [Theory]
    [InlineData(KycLevel.Unknown, false)]
    [InlineData(KycLevel.Unverified, false)]
    [InlineData(KycLevel.Verified, true)]
    [InlineData(KycLevel.Contracted, true)]
    public void KimligiTespitEdilmisSeviyeler(KycLevel level, bool identified)
    {
        level.IsIdentified().ShouldBe(identified);
    }
}

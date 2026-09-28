using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.UnitTests.Policies;

public sealed class KycLimitPolicyTests
{
    private static readonly Currency Try = Currency.From("TRY");
    private static readonly Guid AccountId = Guid.Parse("0a000000-0000-0000-0000-000000000002");

    private static readonly KycLimitPolicy Policy = new(new Dictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>>
    {
        [KycLevel.Unknown] = new Dictionary<KycMovement, decimal>
        {
            [KycMovement.OutgoingTransfer] = 0m,
            [KycMovement.IncomingTransfer] = 0m
        },
        [KycLevel.Unverified] = new Dictionary<KycMovement, decimal>
        {
            [KycMovement.OutgoingTransfer] = 0m,
            [KycMovement.IncomingTransfer] = 2_750m
        }
    });

    private static Money Try_(decimal amount) => new(amount, Try);

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
            KycLevel.Unverified, KycMovement.IncomingTransfer, Try_(750m), Try_(2_000m)));
    }

    /// <summary>
    /// Alıcının limiti aşıldığında gönderene dönen hata alıcının hesabını söylemiyor:
    /// gönderen yalnızca cüzdanı biliyor, arkasındaki hesap onun bilgisi değil.
    /// </summary>
    [Fact]
    public void GelenAylikToplamAsiliyor_AliciyiSoylemedenReddedilir()
    {
        var error = Should.Throw<IncomingLimitExceededException>(() => Policy.EnsureIncoming(
            KycLevel.Unverified, KycMovement.IncomingTransfer, Try_(750.01m), Try_(2_000m)));

        error.LimitName.ShouldBe("Kyc.IncomingTransfer.Monthly");
        error.Message.ShouldNotContain(AccountId.ToString());
    }

    /// <summary>Tarifede olmayan seviye ya da hareket kapalı sayılıyor, açık değil.</summary>
    [Fact]
    public void TanimsizHareket_Kapali()
    {
        Should.Throw<LimitExceededException>(() => Policy.EnsureOutgoing(
            AccountId, KycLevel.Contracted, KycMovement.Payment, Try_(1m), Try_(0m)));
    }
}

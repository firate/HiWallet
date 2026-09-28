using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Handler'ı elle kuran testlerin seviye limitleri: wallet-api'nin ve wallet-consumer'ın
/// appsettings'indeki tarifenin aynısı, her hareket bir arada. Uygulamayı ayağa kaldıran
/// testler tarifeyi appsettings'ten okuyor.
/// </summary>
public static class TestKycLimits
{
    public static KycLimitPolicy Policy { get; } = new(new Dictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>>
    {
        [KycLevel.Unknown] = Limits(incoming: 0m, outgoing: 0m, payment: 0m, withdrawal: 0m),
        [KycLevel.Unverified] = Limits(incoming: 2_750m, outgoing: 0m, payment: 2_750m, withdrawal: 0m),
        [KycLevel.Verified] = Limits(incoming: 50_000m, outgoing: 25_000m, payment: 50_000m, withdrawal: 25_000m),
        [KycLevel.Contracted] = Limits(incoming: 1_000_000m, outgoing: 1_000_000m, payment: 1_000_000m, withdrawal: 1_000_000m)
    });

    private static IReadOnlyDictionary<KycMovement, decimal> Limits(
        decimal incoming, decimal outgoing, decimal payment, decimal withdrawal) =>
        new Dictionary<KycMovement, decimal>
        {
            [KycMovement.IncomingTransfer] = incoming,
            [KycMovement.OutgoingTransfer] = outgoing,
            [KycMovement.Payment] = payment,
            [KycMovement.Withdrawal] = withdrawal
        };
}

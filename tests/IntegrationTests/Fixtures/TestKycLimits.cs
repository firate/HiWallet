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
    public static KycLimitPolicy Policy { get; } = new(
        new Dictionary<KycLevel, IReadOnlyDictionary<KycMovement, decimal>>
        {
            [KycLevel.Unknown] = Limits(
                incoming: 0m, outgoing: 0m, payment: 0m, withdrawal: 0m, deposit: 0m, incomingTotal: 0m),
            [KycLevel.Unverified] = Limits(
                incoming: 5_500m, outgoing: 0m, payment: 5_500m, withdrawal: 0m, deposit: 5_500m, incomingTotal: 5_500m),
            [KycLevel.Verified] = Limits(
                incoming: 50_000m, outgoing: 25_000m, payment: 50_000m, withdrawal: 25_000m,
                deposit: 50_000m, incomingTotal: 100_000m),
            [KycLevel.Contracted] = Limits(
                incoming: 1_000_000m, outgoing: 1_000_000m, payment: 1_000_000m, withdrawal: 1_000_000m,
                deposit: 1_000_000m, incomingTotal: 2_000_000m)
        },
        new Dictionary<KycLevel, decimal>
        {
            [KycLevel.Unknown] = 0m,
            [KycLevel.Unverified] = 5_500m
        });

    private static IReadOnlyDictionary<KycMovement, decimal> Limits(
        decimal incoming, decimal outgoing, decimal payment, decimal withdrawal, decimal deposit, decimal incomingTotal) =>
        new Dictionary<KycMovement, decimal>
        {
            [KycMovement.IncomingTransfer] = incoming,
            [KycMovement.OutgoingTransfer] = outgoing,
            [KycMovement.Payment] = payment,
            [KycMovement.Withdrawal] = withdrawal,
            [KycMovement.Deposit] = deposit,
            [KycMovement.IncomingTotal] = incomingTotal
        };
}

using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Handler'lar <c>IClock</c> alıyor; sabit an vermek için.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

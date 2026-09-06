namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Sabit "şimdi". <c>Microsoft.Extensions.TimeProvider.Testing</c> paketi yerine üç
/// satır: testlerin ihtiyacı yalnızca sabit bir an, zamanı ileri sarmak değil.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

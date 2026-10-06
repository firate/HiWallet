namespace HiWallet.WalletService.Application.Abstractions;

/// <summary>
/// Zaman bir bağımlılıktır. <c>DateTimeOffset.UtcNow</c> doğrudan çağrılırsa günlük limit
/// gibi zamana bağlı kurallar test edilemez hale gelir — "bugün" testin koştuğu güne
/// bağlı olur.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Onboarding'in yerine: hangi sahibin hangi kimlik numarasıyla doğrulandığı. Gerçeği
/// onboarding'e HTTP ile soruyor; burada sınanan şey havalenin kararı, soru değil.
/// </summary>
public sealed class FakeHolderIdentity : IHolderIdentity
{
    private readonly Dictionary<string, string> _nationalIds = [];

    /// <summary>Doluysa her soru bu hatayla düşüyor: onboarding'e ulaşılamıyor.</summary>
    public Exception? Unreachable { get; set; }

    public FakeHolderIdentity Verified(string holder, string nationalId)
    {
        _nationalIds[holder] = nationalId;
        return this;
    }

    public Task<bool> IsHolderAsync(string holder, string nationalId, CancellationToken ct)
    {
        if (Unreachable is not null)
        {
            throw Unreachable;
        }

        return Task.FromResult(_nationalIds.TryGetValue(holder, out var verified) && verified == nationalId);
    }
}

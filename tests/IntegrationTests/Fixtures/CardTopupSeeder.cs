using HiWallet.Shared.Contracts.CardTopups;
using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Application.CardTopups;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Ödenmiş kartla yükleme: canlıdaki yolun aynısı, pay ayrılıyor ve ödendi kapanışı
/// işleniyor. Cüzdana kart parası, sağlayıcının clearing'ine eksi ve beklenen ücret satırı
/// yazılıyor. Settlement ve mutabakat testleri bunun üstüne kuruluyor.
/// </summary>
public static class CardTopupSeeder
{
    public const string Provider = "stripe-fake";

    /// <param name="providerRef">Sağlayıcının ödeme kimliği; settlement kalemleri bununla eşleşiyor.</param>
    /// <returns>Paranın yazıldığı ledger işlemi.</returns>
    public static async Task<Guid> PaidAsync(
        PostgresFixture postgres,
        Guid walletId,
        decimal amount,
        CancellationToken ct,
        string? providerRef = null,
        IClock? clock = null)
    {
        var id = Guid.NewGuid();
        clock ??= new SystemClock();

        await new PlaceCardTopupHoldHandler(
                postgres.ContextFactory, TestKycLimits.Policy, clock, NullLogger<PlaceCardTopupHoldHandler>.Instance)
            .HandleAsync(new PlaceCardTopupHoldCommand(id, walletId, amount, "TRY", Provider), ct);

        var result = await new ProcessCardTopupHandler(
                postgres.ContextFactory, TestProviders.Policy, clock, NullLogger<ProcessCardTopupHandler>.Instance)
            .HandleAsync(new CardTopupClosed
            {
                CardTopupId = id,
                Outcome = CardTopupClosedOutcomes.Paid,
                Provider = Provider,
                ProviderRef = providerRef ?? $"pay_{id:N}",
                Amount = amount,
                Currency = "TRY",
                ClosedAt = clock.UtcNow
            }, ct);

        return result.LedgerTransactionId!.Value;
    }
}

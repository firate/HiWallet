using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Testlerin sağlayıcı tarifesi. appsettings'ten okunmuyor: testin beklediği ücret
/// tutarları o dosya değiştiğinde sessizce kaymasın diye burada AÇIKÇA duruyor —
/// çekim tarifesinde de aynı yaklaşım var.
/// </summary>
public static class TestProviders
{
    /// <summary>Kart sağlayıcısı: oran + sabit, ücreti settlement anında kesiyor.</summary>
    public const decimal StripeRate = 0.029m;

    public const decimal StripeFixed = 0.30m;

    /// <summary>Banka: yalnızca sabit ücret, dönem sonu faturasıyla alıyor.</summary>
    public const decimal BankFixed = 1.50m;

    public static ProviderPolicy Policy { get; } = new(new Dictionary<string, ProviderTerms>
    {
        ["stripe-fake"] = new(
            "stripe-fake", FeeSettlement.Net, new ProviderFeeTariff(StripeRate, StripeFixed)),

        ["bank-fake"] = new(
            "bank-fake", FeeSettlement.Invoiced, new ProviderFeeTariff(Rate: 0m, BankFixed))
    });
}

using System.Globalization;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// Testlerin cep numaraları. Bir numara tek müşteride: testler aynı numarayı paylaşsaydı
/// birbirinin doğrulamasını reddettirirdi.
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Rastgele bir cep numarası, müşterinin yazdığı biçimde: <c>05xxxxxxxxx</c>.</summary>
    public static string New() =>
        "05" + Random.Shared.NextInt64(100_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture);

    /// <summary>SMS sağlayıcısına giden biçim: <c>+905xxxxxxxxx</c>.</summary>
    public static string E164(string local) => "+90" + local[1..];
}

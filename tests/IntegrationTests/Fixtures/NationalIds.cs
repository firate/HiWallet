namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Kurala uyan kimlik numaraları: bir numara tek müşteride, her test kendi numarasıyla.</summary>
public static class NationalIds
{
    /// <summary>Rastgele ilk dokuz hane, kontrol haneleri kurala göre.</summary>
    public static string New()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);

        for (var i = 1; i < 9; i++)
        {
            d[i] = Random.Shared.Next(0, 10);
        }

        d[9] = (((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10 + 10) % 10;
        d[10] = d[..10].Sum() % 10;
        return string.Concat(d);
    }
}

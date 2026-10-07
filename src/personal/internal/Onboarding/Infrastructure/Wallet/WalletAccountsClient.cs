using System.Net.Http.Json;
using System.Text.Json;

namespace HiWallet.Onboarding.Infrastructure.Wallet;

/// <summary>
/// wallet-api'nin onboarding'e açık iki ucu, servisin kendi token'ıyla. Kayıt anında
/// müşterinin henüz token'ı yok; hesabın kime açılacağını bu servis söylüyor.
/// </summary>
public sealed class WalletAccountsClient(HttpClient http)
{
    /// <summary>Kimliğin bireysel hesabı; tekrar edilebilir, aynı hesap döner.</summary>
    public async Task<Guid> OpenPersonAccountAsync(string holder, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("v1/person-accounts", new { holder }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("accountId").GetGuid();
    }

    /// <summary>Seviyeyi yükseltir; hesabın ulaştığı seviyeyi döner.</summary>
    public async Task<string> RaiseKycLevelAsync(Guid accountId, string level, CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync($"v1/accounts/{accountId}/kyc-level", new { level }, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("kycLevel").GetString()!;
    }
}

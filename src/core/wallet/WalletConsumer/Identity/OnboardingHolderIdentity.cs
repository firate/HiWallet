using System.Net.Http.Json;
using HiWallet.WalletService.Application.Abstractions;

namespace HiWallet.WalletConsumer.Identity;

/// <summary>
/// Kimlik numarasının hesap sahibine ait olup olmadığını onboarding'e soruyor. Numara
/// gövdede gidiyor, adreste DEĞİL: adres erişim log'larına düşüyor. Cevap yalnızca evet
/// ya da hayır; numara burada saklanmıyor, log'a da yazılmıyor.
///
/// Cevap alınamazsa istisna yukarı çıkıyor: havale kuyruğa geri dönüyor, tahminle
/// karar verilmiyor.
/// </summary>
internal sealed class OnboardingHolderIdentity(HttpClient http) : IHolderIdentity
{
    public async Task<bool> IsHolderAsync(string holder, string nationalId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("v1/holder-checks", new HolderCheckRequest(holder, nationalId), ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HolderCheckResponse>(ct)
                   ?? throw new InvalidOperationException("Onboarding boş cevap döndü.");

        return body.Matches;
    }

    // Onboarding'in sözleşmesi; paylaşılan bir assembly'den gelmiyor (ön API'lerle aynı
    // kalıp): iki taraf ayrı yazıyor, biri değiştiğinde test kırılıyor.
    private sealed record HolderCheckRequest(string Holder, string NationalId);

    private sealed record HolderCheckResponse(bool Matches);
}

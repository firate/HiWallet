using System.Net.Http.Json;
using HiWallet.Onboarding.Application.Abstractions;
using HiWallet.Onboarding.Domain;

namespace HiWallet.Onboarding.Infrastructure.Messaging;

/// <summary>
/// SMS sağlayıcısının HTTP API'si. İstek tipleri burada, sağlayıcı tarafında ayrı
/// yazılıyor (banka entegrasyonundaki gerekçe: gerçek entegrasyonda tipler sağlayıcının
/// dokümanından geliyor).
/// </summary>
internal sealed class SmsApiClient(HttpClient http) : ISmsSender
{
    public async Task SendAsync(PhoneNumber to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("v1/messages", new SendSmsRequest(to.Value, text), ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed record SendSmsRequest(string To, string Text);
}

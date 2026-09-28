using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.Nvi.Fake;
using HiWallet.Onboarding.Domain;
using HiWallet.Onboarding.Infrastructure.Messaging;
using HiWallet.Onboarding.Infrastructure.PopulationRegistry;
using HiWallet.Sms.Fake;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HiWallet.IntegrationTests.Onboarding;

/// <summary>
/// onboarding'in SMS ve nüfus kaydı istemcileri, sahteleriyle HTTP üzerinden. İstek ve
/// cevap tipleri iki tarafta ayrı yazılıyor; alan adı ya da durum kodu kayarsa burada
/// düşüyor.
/// </summary>
public sealed class ExternalServiceContractTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<SmsFakeApp> _sms = new();
    private readonly WebApplicationFactory<NviFakeApp> _nvi = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _sms.DisposeAsync();
        await _nvi.DisposeAsync();
    }

    /// <summary>Sahte sağlayıcı mesajı gönderilmiş gibi tutuyor; deneyen kişi kodu oradan okuyor.</summary>
    [Fact]
    public async Task Sms_GonderilenMesajKutudanOkunur()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = new SmsApiClient(_sms.CreateClient());
        var phone = PhoneNumber.Parse("05329876543");

        await sender.SendAsync(phone, "HiWallet doğrulama kodun: 482913.", ct);

        using var reader = _sms.CreateClient();
        var inbox = await reader.GetFromJsonAsync<JsonElement>($"/v1/messages?to={Uri.EscapeDataString(phone.Value)}", ct);
        inbox.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("text").GetString())
            .ShouldContain("HiWallet doğrulama kodun: 482913.");
    }

    [Fact]
    public async Task NufusKaydi_VarsayilanEslesir_SenaryoIleEslesmez()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new PopulationRegistryClient(_nvi.CreateClient());
        var matching = NationalId.Parse("10000000146");
        var mismatching = NationalId.Parse("12345678950");

        using var tester = _nvi.CreateClient();
        (await tester.PostAsJsonAsync("/v1/scenarios", new { nationalId = mismatching.Value, outcome = "Mismatch" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await registry.MatchesAsync(matching, "Ayşe", "Yılmaz", 1990, ct)).ShouldBeTrue();
        (await registry.MatchesAsync(mismatching, "Ayşe", "Yılmaz", 1990, ct)).ShouldBeFalse();
    }
}

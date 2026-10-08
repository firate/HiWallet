using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Bir cüzdanın çekimleri, yeniden eskiye. Orchestrator hesabın kullanıcılarını bilmiyor:
/// müşteri yalnızca kendi başlattığı çekimleri görüyor, çalışan cüzdanın bütün çekimlerini.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WalletWithdrawalsApiTests(OrchestratorFixture fixture) : IAsyncLifetime
{
    private const string Iban = "TR33 0006 1005 1978 6457 8413 26";

    private WithdrawalOrchestratorApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WithdrawalOrchestratorApiFactory(fixture);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> WithdrawAsync(HttpClient client, Guid walletId, decimal amount, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId = Guid.NewGuid(), walletId, amount, currency = "TRY", destinationIban = Iban
            }),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString("N") } }
        };

        var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("withdrawalId").GetGuid();
    }

    private static Guid[] Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("withdrawalId").GetGuid())];

    [Fact]
    public async Task Musteri_YalnizcaKendiBaslattigiCekimleriYenidenEskiyeGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = Guid.NewGuid();
        using var owner = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");
        using var other = _factory.CreateClient().As($"baska-{Guid.NewGuid():N}");

        var first = await WithdrawAsync(owner, walletId, 100m, ct);
        var second = await WithdrawAsync(owner, walletId, 200m, ct);
        var third = await WithdrawAsync(owner, walletId, 300m, ct);
        await WithdrawAsync(other, walletId, 50m, ct);

        var page = await owner.GetFromJsonAsync<JsonElement>($"/v1/wallets/{walletId}/withdrawals", ct);

        Ids(page).ShouldBe([third, second, first]);
        page.GetProperty("items")[0].GetProperty("destinationIban").GetString()!.ShouldNotContain("6457");
    }

    [Fact]
    public async Task Sayfalama_CursorIleDevamEder()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = Guid.NewGuid();
        using var owner = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");

        var first = await WithdrawAsync(owner, walletId, 100m, ct);
        var second = await WithdrawAsync(owner, walletId, 200m, ct);
        var third = await WithdrawAsync(owner, walletId, 300m, ct);

        var page = await owner.GetFromJsonAsync<JsonElement>($"/v1/wallets/{walletId}/withdrawals?size=2", ct);
        var cursor = page.GetProperty("nextCursor").GetGuid();
        var next = await owner.GetFromJsonAsync<JsonElement>(
            $"/v1/wallets/{walletId}/withdrawals?size=2&after={cursor}", ct);

        Ids(page).ShouldBe([third, second]);
        Ids(next).ShouldBe([first]);
        next.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Calisan_CuzdaninButunCekimleriniGorur()
    {
        var ct = TestContext.Current.CancellationToken;
        var walletId = Guid.NewGuid();
        using var owner = _factory.CreateClient().As($"musteri-{Guid.NewGuid():N}");
        using var other = _factory.CreateClient().As($"baska-{Guid.NewGuid():N}");
        using var staff = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}", StaffPermissions.CustomerView);
        using var unauthorized = _factory.CreateClient().AsStaff($"calisan-{Guid.NewGuid():N}");

        await WithdrawAsync(owner, walletId, 100m, ct);
        await WithdrawAsync(other, walletId, 50m, ct);

        var page = await staff.GetFromJsonAsync<JsonElement>($"/v1/wallets/{walletId}/withdrawals", ct);
        var refused = await unauthorized.GetAsync($"/v1/wallets/{walletId}/withdrawals", ct);

        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

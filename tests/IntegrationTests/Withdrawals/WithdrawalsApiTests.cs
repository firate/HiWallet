using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// <c>POST /v1/withdrawals</c> ve durum sorgusu. Uygulama gerçek haliyle ayakta:
/// doğrulama, IBAN kontrolü, saga ve outbox yazımı zinciri baştan sona koşuyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WithdrawalsApiTests(OrchestratorFixture fixture) : IAsyncLifetime
{
    private const string ValidIban = "TR33 0006 1005 1978 6457 8413 26";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WithdrawalOrchestratorApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new WithdrawalOrchestratorApiFactory(fixture);
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GecerliIstek_202Doner_SagaVeKomutYazilir()
    {
        var accountId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        var response = await PostAsync(accountId, walletId, "ilk-cekim");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();

        var body = await ReadAcceptedAsync(response);

        body.State.ShouldBe("initiated");
        body.Replayed.ShouldBeFalse();

        await using var db = fixture.CreateContext();

        var saga = await db.Sagas.SingleAsync(
            s => s.Id == body.WithdrawalId, TestContext.Current.CancellationToken);

        saga.AccountId.ShouldBe(accountId);
        saga.WalletId.ShouldBe(walletId);
        saga.Amount.ShouldBe(250.75m);
        saga.State.ShouldBe(WithdrawalState.Initiated);

        // Boşluklu gönderildi, normalize edilmiş halde durmalı.
        saga.Destination.Value.ShouldBe("TR330006100519786457841326");

        var message = await db.Outbox.SingleAsync(
            m => m.SagaId == saga.Id, TestContext.Current.CancellationToken);

        message.RoutingKey.ShouldBe("DebitForWithdrawal");
        message.PublishedAt.ShouldBeNull();

        // Satır kimliği ile komuttaki CommandId ayrışamaz: ayrışsalardı relay'in
        // ikinci teslimi wallet'ta yeni bir komut gibi görünür ve müşteri iki kez
        // para çekerdi.
        using var payload = JsonDocument.Parse(message.Payload);

        payload.RootElement.GetProperty("commandId").GetGuid().ShouldBe(message.Id);
        payload.RootElement.GetProperty("sagaId").GetGuid().ShouldBe(saga.Id);

        // Komisyon TAŞINMIYOR: politikayı wallet uyguluyor.
        payload.RootElement.GetProperty("amount").GetDecimal().ShouldBe(250.75m);
        payload.RootElement.TryGetProperty("commission", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Asıl korunan şey ikinci outbox satırının YAZILMAMASI. Yazılsaydı wallet'a
    /// ikinci bir düşme komutu giderdi ve müşteri iki kez para çekerdi.
    /// </summary>
    [Fact]
    public async Task AyniAnahtar_IkinciIstek_YeniCekimAcmaz()
    {
        var accountId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        var first = await ReadAcceptedAsync(await PostAsync(accountId, walletId, "tekrar-eden"));
        var secondResponse = await PostAsync(accountId, walletId, "tekrar-eden");

        // Tekrar da başarılı bir sonuç, hata değil.
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var second = await ReadAcceptedAsync(secondResponse);

        second.WithdrawalId.ShouldBe(first.WithdrawalId);
        second.Replayed.ShouldBeTrue();

        await using var db = fixture.CreateContext();

        (await db.Sagas.CountAsync(
            s => s.AccountId == accountId, TestContext.Current.CancellationToken)).ShouldBe(1);

        (await db.Outbox.CountAsync(
            m => m.SagaId == first.WithdrawalId, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task FarkliHesap_AyniAnahtar_YeniCekimAcar()
    {
        var walletId = Guid.NewGuid();

        var first = await ReadAcceptedAsync(await PostAsync(Guid.NewGuid(), walletId, "ortak-anahtar"));
        var second = await ReadAcceptedAsync(await PostAsync(Guid.NewGuid(), walletId, "ortak-anahtar"));

        second.WithdrawalId.ShouldNotBe(first.WithdrawalId);
        second.Replayed.ShouldBeFalse();
    }

    /// <summary>
    /// Transfer'de anahtar opsiyonel, burada değil: çekim çok adımlı ve dışarıya para
    /// çıkarıyor, anahtarsız bir tekrar ikinci bir banka transferi başlatırdı.
    /// </summary>
    [Fact]
    public async Task IdempotencyKeyYok_400Doner()
    {
        var request = new
        {
            accountId = Guid.NewGuid(),
            walletId = Guid.NewGuid(),
            amount = 100m,
            currency = "TRY",
            destinationIban = ValidIban
        };

        var response = await _client.PostAsJsonAsync(
            "/v1/withdrawals", request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("TR33 0006 1005 1978 6457 8413 27")] // checksum tutmuyor
    [InlineData("TR00 0006 1005 1978 6457 8413 26")] // kontrol basamağı 00
    [InlineData("XX")]
    [InlineData("")]
    public async Task GecersizIban_400Doner_VeHicbirSeyYazilmaz(string iban)
    {
        var accountId = Guid.NewGuid();

        var response = await PostAsync(accountId, Guid.NewGuid(), "gecersiz-iban", iban: iban);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = fixture.CreateContext();

        (await db.Sagas.AnyAsync(
            s => s.AccountId == accountId, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task NegatifTutar_400Doner()
    {
        var response = await PostAsync(
            Guid.NewGuid(), Guid.NewGuid(), "negatif", amount: -5m);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_MevcutCekim_DurumuDoner_IbanMaskeli()
    {
        var accepted = await ReadAcceptedAsync(
            await PostAsync(Guid.NewGuid(), Guid.NewGuid(), "sorgu"));

        var response = await _client.GetAsync(
            $"/v1/withdrawals/{accepted.WithdrawalId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WithdrawalBody>(
            JsonOptions, TestContext.Current.CancellationToken);

        body.ShouldNotBeNull();
        body.State.ShouldBe("initiated");
        body.Amount.ShouldBe(250.75m);
        body.TotalDebited.ShouldBeNull();

        // Tam IBAN yanıtta dolaşmıyor: log'a, hata izlemeye ve tarayıcı geçmişine düşer.
        body.DestinationIban.ShouldBe("TR33******************1326");
        body.DestinationIban.ShouldNotContain("6457");
    }

    [Fact]
    public async Task Get_OlmayanCekim_404Doner()
    {
        var response = await _client.GetAsync(
            $"/v1/withdrawals/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> PostAsync(
        Guid accountId,
        Guid walletId,
        string idempotencyKey,
        string iban = ValidIban,
        decimal amount = 250.75m)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/withdrawals")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                walletId,
                amount,
                currency = "TRY",
                destinationIban = iban
            })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<AcceptedBody> ReadAcceptedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<AcceptedBody>(
            JsonOptions, TestContext.Current.CancellationToken);

        return body.ShouldNotBeNull();
    }

    private sealed record AcceptedBody(Guid WithdrawalId, string State, bool Replayed);

    private sealed record WithdrawalBody(
        Guid WithdrawalId,
        string State,
        decimal Amount,
        string Currency,
        string DestinationIban,
        decimal? TotalDebited,
        string? FailureReason);
}

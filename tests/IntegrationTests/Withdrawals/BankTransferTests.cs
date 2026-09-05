using System.Text.Json;
using System.Text.Json.Nodes;
using HiWallet.BankService.Application;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Sahte bankanın davranışı. Broker gerekmiyor — komut doğrudan handler'a veriliyor.
/// Sınanan şey taşıma değil, dört senaryonun doğru sonucu üretmesi ve idempotency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankTransferTests(BankFixture bank)
{
    private const string Iban = "TR330006100519786457841326";

    /// <param name="defaultOutcome">
    /// Senaryosu kurulmamış saga için varsayılan. Testlerin çoğu senaryoyu açıkça
    /// kuruyor; bu parametre varsayılanın kendisini sınayan test için.
    /// </param>
    private StartBankTransferHandler Handler(TransferOutcome? defaultOutcome = null) => new(
        bank.ContextFactory,
        Options.Create(new BankOptions
        {
            DefaultOutcome = defaultOutcome ?? TransferOutcome.Success
        }),
        TimeProvider.System,
        NullLogger<StartBankTransferHandler>.Instance);

    private ScenarioStore Scenarios() => new(bank.ContextFactory, TimeProvider.System);

    /// <summary>
    /// Varsayılanın başarı olması testlerin yalnızca ilgilendikleri sapmayı
    /// kurmasını sağlıyor.
    /// </summary>
    [Fact]
    public async Task SenaryoYoksa_TransferBasarili()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        var reply = await Handler().HandleAsync(command, ct);

        reply.RoutingKey.ShouldBe(nameof(BankTransferSucceeded));
        reply.Replayed.ShouldBeFalse();

        var payload = JsonNode.Parse(reply.Payload)!;
        payload["sagaId"]!.GetValue<Guid>().ShouldBe(command.SagaId);
        payload["bankReference"]!.GetValue<string>().ShouldStartWith("BNK-");
    }

    [Fact]
    public async Task KaliciHata_BankTransferFailedDoner()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await Scenarios().ArmAsync(
            command.SagaId, TransferOutcome.PermanentFailure, 0, 0, ct);

        var reply = await Handler().HandleAsync(command, ct);

        reply.RoutingKey.ShouldBe(nameof(BankTransferFailed));

        JsonNode.Parse(reply.Payload)!["reason"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

        await using var db = bank.CreateContext();
        var transfer = await db.Transfers.SingleAsync(t => t.CommandId == command.CommandId, ct);

        transfer.BankReference.ShouldBeNull();
        transfer.FailureReason.ShouldNotBeNull();
    }

    /// <summary>
    /// Geçici hata bir CEVAP DEĞİL: saga'ya hiçbir şey bildirilmiyor, mesaj kuyruğa
    /// geri konsun diye istisna fırlatılıyor. Cevap üretilseydi her ağ kesintisinde
    /// telafi başlar ve müşterinin parası ileri geri taşınırdı.
    /// </summary>
    [Fact]
    public async Task GeciciHata_CevapUretmez_SayaciAzaltir()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await Scenarios().ArmAsync(
            command.SagaId, TransferOutcome.TransientFailure, transientFailures: 2, 0, ct);

        await Should.ThrowAsync<TransientBankFailureException>(
            () => Handler().HandleAsync(command, ct));

        var state = await Scenarios().FindAsync(command.SagaId, ct);

        state.ShouldNotBeNull();
        state.RemainingTransientFailures.ShouldBe(1);
        state.Attempts.ShouldBe(1);

        // Transfer kaydı YOK: bir şey olmadı, dolayısıyla idempotency defterine de
        // yazılmadı. Yazılsaydı sonraki teslim "zaten işlendi" der ve transfer hiç
        // yapılmazdı.
        await using var db = bank.CreateContext();
        (await db.Transfers.AnyAsync(t => t.CommandId == command.CommandId, ct)).ShouldBeFalse();
    }

    /// <summary>
    /// "Transient sonra başarılı" (overview.md madde 9): retry'ın gerçekten devreye
    /// girip sonunda başardığının kanıtı.
    /// </summary>
    [Fact]
    public async Task GeciciHataKotasiDolunca_TransferBasarili()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await Scenarios().ArmAsync(
            command.SagaId, TransferOutcome.TransientFailure, transientFailures: 2, 0, ct);

        await Should.ThrowAsync<TransientBankFailureException>(
            () => Handler().HandleAsync(command, ct));
        await Should.ThrowAsync<TransientBankFailureException>(
            () => Handler().HandleAsync(command, ct));

        var reply = await Handler().HandleAsync(command, ct);

        reply.RoutingKey.ShouldBe(nameof(BankTransferSucceeded));

        await using var db = bank.CreateContext();
        var transfer = await db.Transfers.SingleAsync(t => t.CommandId == command.CommandId, ct);

        // Üçüncü denemede sonuçlandı; sayaç bunu gösterebilmeli.
        transfer.Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task GecikmeliBasari_BeklerAmaBasarir()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await Scenarios().ArmAsync(
            command.SagaId, TransferOutcome.DelayedSuccess, 0, delayMilliseconds: 300, ct);

        var started = DateTimeOffset.UtcNow;
        var reply = await Handler().HandleAsync(command, ct);
        var elapsed = DateTimeOffset.UtcNow - started;

        reply.RoutingKey.ShouldBe(nameof(BankTransferSucceeded));
        elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    /// Bankanın idempotency anahtarı <c>CommandId</c>. Aynı komut iki kez gelirse
    /// para İKİ KEZ gitmemeli — tekrar teslim broker'ın normal davranışı.
    /// </summary>
    [Fact]
    public async Task AyniKomut_IkinciKez_TransferTekrarlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        var first = await Handler().HandleAsync(command, ct);
        var second = await Handler().HandleAsync(command, ct);

        second.Replayed.ShouldBeTrue();
        second.RoutingKey.ShouldBe(first.RoutingKey);

        // jsonb anahtar sırasını normalize ediyor; karşılaştırma ayrıştırılmış hal
        // üzerinden.
        JsonNode.DeepEquals(JsonNode.Parse(second.Payload), JsonNode.Parse(first.Payload))
            .ShouldBeTrue();

        await using var db = bank.CreateContext();
        (await db.Transfers.CountAsync(t => t.SagaId == command.SagaId, ct)).ShouldBe(1);
    }

    /// <summary>
    /// Referans komuttan TÜRETİLİYOR, rastgele değil: gerçek bir bankada da aynı
    /// idempotency anahtarı aynı transferi işaret eder.
    /// </summary>
    [Fact]
    public async Task BankaReferansi_AyniKomutIcinAyni()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        var first = await Handler().HandleAsync(command, ct);
        var second = await Handler().HandleAsync(command, ct);

        Reference(first).ShouldBe(Reference(second));
    }

    /// <summary>
    /// Senaryo saga başına: aynı IBAN'a giden başka bir çekim etkilenmemeli.
    /// IBAN'a bağlansaydı paralel testler birbirini bozardı.
    /// </summary>
    [Fact]
    public async Task Senaryo_YalnizcaKendiSagasiniEtkiler()
    {
        var ct = TestContext.Current.CancellationToken;
        var failing = Command();
        var healthy = Command();

        await Scenarios().ArmAsync(failing.SagaId, TransferOutcome.PermanentFailure, 0, 0, ct);

        (await Handler().HandleAsync(failing, ct)).RoutingKey.ShouldBe(nameof(BankTransferFailed));
        (await Handler().HandleAsync(healthy, ct)).RoutingKey.ShouldBe(nameof(BankTransferSucceeded));
    }

    private static string Reference(BankReply reply) =>
        JsonNode.Parse(reply.Payload)!["bankReference"]!.GetValue<string>();

    private static StartBankTransfer Command() => new()
    {
        CommandId = Guid.NewGuid(),
        SagaId = Guid.NewGuid(),
        Amount = 250.75m,
        Currency = "TRY",
        DestinationIban = Iban
    };
}

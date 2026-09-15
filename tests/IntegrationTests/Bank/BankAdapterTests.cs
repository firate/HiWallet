using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HiWallet.Bank.Fake.Application;
using HiWallet.BankAdapter.Application;
using HiWallet.BankIntegration.Persistence;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Adaptörün banka ile bizim aramızdaki davranışı. Broker gerekmiyor: komut
/// doğrudan handler'a veriliyor, sınanan şey taşıma değil akışın ŞEKLİ.
///
/// <b>Bu dosyanın asıl konusu tek bir şey:</b> banka çağrısı artık cevap
/// üretmiyor. Transfer <c>pending</c> yazılıyor, saga bekliyor ve sonuç sonra
/// geliyor (decisions.md madde 35).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankAdapterTests(BankFixture bankDb, BankFakeFixture bankFakeDb) : IAsyncLifetime
{
    private const string Iban = "TR330006100519786457841326";

    private BankFakeFactory _bankFake = null!;
    private BankAdapterFactory _adapter = null!;

    public ValueTask InitializeAsync()
    {
        _bankFake = new BankFakeFactory(bankFakeDb);
        _adapter = new BankAdapterFactory(bankDb, "http://bank-fake", _bankFake.CreateClient());

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _adapter.DisposeAsync();
        await _bankFake.DisposeAsync();
    }

    private static StartBankTransfer Command() => new()
    {
        CommandId = Guid.NewGuid(),
        SagaId = Guid.NewGuid(),
        Amount = 100m,
        Currency = "TRY",
        DestinationIban = Iban
    };

    private async Task HandleAsync(StartBankTransfer command, CancellationToken ct)
    {
        using var scope = _adapter.Services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<StartBankTransferHandler>()
            .HandleAsync(command, ct);
    }

    private async Task<BankTransfer?> FindAsync(Guid commandId, CancellationToken ct)
    {
        await using var db = bankDb.CreateContext();

        return await db.Transfers.AsNoTracking().FirstOrDefaultAsync(t => t.CommandId == commandId, ct);
    }

    /// <summary>
    /// <b>Değişikliğin özü.</b> Banka komutu kabul etti ama sonuç YOK: satır
    /// <c>pending</c>, cevap saklanmamış ve yayınlanacak bir şey de yok.
    ///
    /// Eskiden burada <c>BankTransferSucceeded</c> anında üretiliyordu ve saga
    /// <c>bank_transfer_pending</c>'den hiç geçmiyordu.
    /// </summary>
    [Fact]
    public async Task Komut_TransferiBaslatir_CevapURETMEZ()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await HandleAsync(command, ct);

        var transfer = await FindAsync(command.CommandId, ct);

        transfer.ShouldNotBeNull();
        transfer.Status.ShouldBe("pending");
        transfer.BankReference.ShouldNotBeNullOrWhiteSpace("banka kabul ettiyse referans dönmüş olmalı");
        transfer.SagaId.ShouldBe(command.SagaId);

        transfer.ResolvedAt.ShouldBeNull("sonuç bu çağrıda öğrenilmiyor");
        transfer.ReplyRoutingKey.ShouldBeNull("cevap henüz yok");
        transfer.ReplyPublishedAt.ShouldBeNull();
    }

    /// <summary>
    /// Komut ikinci kez teslim edildiğinde banka İKİNCİ KEZ ARANMIYOR. Aransaydı
    /// bankanın kendi idempotency'si kurtarırdı — ama ona güvenmek bizim tarafta
    /// koruma olmadığı anlamına gelirdi.
    /// </summary>
    [Fact]
    public async Task AyniKomut_IkinciKez_TransferTekrarlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await HandleAsync(command, ct);
        var first = await FindAsync(command.CommandId, ct);

        await HandleAsync(command, ct);
        var second = await FindAsync(command.CommandId, ct);

        second!.BankReference.ShouldBe(first!.BankReference);
        second.StartedAt.ShouldBe(first.StartedAt);

        await using var fake = bankFakeDb.CreateContext();

        (await fake.Transfers.CountAsync(t => t.ClientReference == command.SagaId.ToString(), ct))
            .ShouldBe(1, "bankada tek transfer açılmalı");
    }

    /// <summary>
    /// Banka cevap veremediğinde <see cref="TransientBankException"/> çıkıyor ve
    /// KAYIT AÇILMIYOR.
    ///
    /// Kayıt açılsaydı tekrar teslimde dedup "zaten işlendi" der ve banka hiç
    /// aranmazdı — saga sonsuza kadar beklerdi. Bu yüzden sıra "önce banka, sonra
    /// kayıt".
    /// </summary>
    [Fact]
    public async Task BankaCevapVermiyor_GeciciHata_KayitAcilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await ArmAsync(command.SagaId.ToString(), TransferOutcome.TransientFailure);

        await Should.ThrowAsync<TransientBankException>(() => HandleAsync(command, ct));

        (await FindAsync(command.CommandId, ct))
            .ShouldBeNull("geçici hatada satır yazılmamalı, yoksa tekrar teslimde banka hiç aranmaz");

        // Kota doldu: ikinci deneme geçiyor. Tüketici mesajı requeue ettiğinde
        // olan tam olarak bu.
        await HandleAsync(command, ct);

        (await FindAsync(command.CommandId, ct)).ShouldNotBeNull();
    }

    /// <summary>
    /// Kalıcı başarısızlık sonucu ledger'a değil saga'ya gidiyor: cevap
    /// <c>BankTransferFailed</c> olarak SAKLANIYOR, yayını relay yapıyor.
    /// </summary>
    [Fact]
    public async Task KaliciHata_CevapBankTransferFailedOlarakSaklanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await ArmAsync(command.SagaId.ToString(), TransferOutcome.Failure);
        await HandleAsync(command, ct);

        var transfer = await FindAsync(command.CommandId, ct);

        await ResolveViaBankAsync(transfer!.BankReference!, ct);

        var resolved = await FindAsync(command.CommandId, ct);

        resolved!.Status.ShouldBe("failed");
        resolved.ReplyRoutingKey.ShouldBe(nameof(BankTransferFailed));
        resolved.FailureReason.ShouldNotBeNullOrWhiteSpace();

        var payload = JsonDocument.Parse(resolved.ReplyPayload!).RootElement;
        payload.GetProperty("sagaId").GetGuid().ShouldBe(command.SagaId);
    }

    /// <summary>
    /// Mutabakat yolu transferi kapatıyor ve bunu <c>resolved_via</c>'ya yazıyor.
    ///
    /// O kolonun işleyişe etkisi yok ama gözlem değeri yüksek: callback hattı
    /// sağlıklıyken burada <c>reconciliation</c> görünmemeli (decisions.md madde 35).
    /// </summary>
    [Fact]
    public async Task MutabakatYolu_TransferiKapatir_VeIsaretler()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await HandleAsync(command, ct);

        var transfer = await FindAsync(command.CommandId, ct);
        await ResolveViaBankAsync(transfer!.BankReference!, ct);

        var resolved = await FindAsync(command.CommandId, ct);

        resolved!.Status.ShouldBe("succeeded");
        resolved.ResolvedVia.ShouldBe("reconciliation");
        resolved.ReplyRoutingKey.ShouldBe(nameof(BankTransferSucceeded));
        resolved.Fee.ShouldNotBeNull();

        resolved.ReplyPublishedAt.ShouldBeNull(
            "kapatmak yayınlamak değil; yayın relay'in ayrı işi");
    }

    /// <summary>
    /// İkinci kez kapatma girişimi zararsız. Callback geç gelip tarama önce
    /// bulduğunda olan tam olarak bu — ve tersi de.
    /// </summary>
    [Fact]
    public async Task IkinciKapatma_YokSayilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = Command();

        await HandleAsync(command, ct);
        var transfer = await FindAsync(command.CommandId, ct);

        (await ResolveViaBankAsync(transfer!.BankReference!, ct)).ShouldBeTrue();
        (await ResolveViaBankAsync(transfer.BankReference!, ct)).ShouldBeFalse();
    }

    /// <summary>
    /// Bankanın bilmediğimiz bir referans için gönderdiği sonuç UYDURULMUYOR:
    /// cevap üretmek, hiç göndermediğimiz bir transferi tamamlanmış göstermek olurdu.
    /// </summary>
    [Fact]
    public async Task TaninmayanReferans_CevapUretmez()
    {
        var ct = TestContext.Current.CancellationToken;

        var completer = _adapter.Services.GetRequiredService<TransferCompleter>();

        var resolved = await completer.ResolveAsync(
            "BNK-BOYLE-BIR-SEY-YOK",
            BankIntegration.Domain.BankTransferStatus.Succeeded,
            fee: 1m,
            failureReason: null,
            TransferCompleter.ViaCallback,
            ct);

        resolved.ShouldBeFalse();
    }

    /// <summary>
    /// Sahte bankanın sonucu belli olana kadar bekleyip adaptöre kapattırır.
    /// Mutabakat taramasının yaptığının aynısı; zamanlanmış işi beklemeden.
    /// </summary>
    private async Task<bool> ResolveViaBankAsync(string bankReference, CancellationToken ct)
    {
        var client = _adapter.Services.GetRequiredService<BankClient>();
        var completer = _adapter.Services.GetRequiredService<TransferCompleter>();

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var status = await client.GetStatusAsync(bankReference, ct);
            var mapped = status is null ? null : BankClient.Map(status.Status);

            if (mapped is { } value && value is not BankIntegration.Domain.BankTransferStatus.Pending)
            {
                return await completer.ResolveAsync(
                    bankReference, value, status!.Fee, status.FailureReason,
                    TransferCompleter.ViaReconciliation, ct);
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"{bankReference} beş saniyede sonuçlanmadı.");
    }

    private async Task ArmAsync(string clientReference, TransferOutcome outcome)
    {
        using var client = _bankFake.CreateClient();

        var response = await client.PostAsync(
            "/v1/scenarios",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    clientReference,
                    outcome = outcome.ToString(),
                    transientFailures = 1,
                    delayMilliseconds = 0
                }),
                Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}

/// <summary>
/// Testlerde bankanın imzasını üretir. Doğrulayıcının kendisiyle PAYLAŞILMIYOR —
/// paylaşılsaydı test, doğrulayıcının kendi hatasını da kopyalayarak "doğru"
/// bulurdu.
/// </summary>
internal static class TestCallbackSigner
{
    public static string Sign(byte[] body, string secret) =>
        "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
}

using System.Text.Json;
using HiWallet.BankAdapter.Application;
using HiWallet.BankIntegration.Persistence;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Contracts.DepositReturns;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HiWallet.IntegrationTests.Bank;

/// <summary>
/// Askıdaki havalenin iadesinin banka tarafı. Gönderenin IBAN'ı komutta yok: adaptör havaleyi
/// kendi kaydında bankanın referansıyla bulup IBAN'ı oradan okuyor. Transfer çekimdekiyle aynı
/// satıra yazılıyor ve sonucu aynı yoldan geliyor; satır iade edilen havaleye bağlı.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BankDepositReturnTests(BankFixture bankDb) : IAsyncLifetime
{
    private const string SenderIban = "TR330006100519786457841326";

    private BankFakeFactory _bankFake = null!;
    private BankAdapterFactory _adapter = null!;

    public ValueTask InitializeAsync()
    {
        _bankFake = new BankFakeFactory();
        _adapter = new BankAdapterFactory(bankDb, "http://bank-fake", _bankFake.CreateClient());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _adapter.DisposeAsync();
        await _bankFake.DisposeAsync();
    }

    private async Task<BankDeposit> DepositAsync(string? senderIban, CancellationToken ct)
    {
        var deposit = new BankDeposit
        {
            Id = Guid.NewGuid(),
            Provider = TestBankSecrets.Bank,
            BankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
            Amount = 80m,
            Currency = "TRY",
            Description = "kira",
            SenderName = "Ayşe Yılmaz",
            SenderIban = senderIban,
            SenderNationalId = "10000000146",
            ReceivedAt = DateTimeOffset.UtcNow,
            DiscoveredAt = DateTimeOffset.UtcNow,
            DiscoveredVia = DepositRecorder.ViaCallback,
            Payload = "{}",
            PublishedAt = DateTimeOffset.UtcNow
        };

        await using var db = bankDb.CreateContext();
        db.Deposits.Add(deposit);
        await db.SaveChangesAsync(ct);

        return deposit;
    }

    private static ReturnBankDeposit Command(BankDeposit deposit) => new()
    {
        CommandId = Guid.NewGuid(),
        SagaId = Guid.NewGuid(),
        Provider = deposit.Provider,
        DepositBankReference = deposit.BankReference,
        Amount = deposit.Amount,
        Currency = deposit.Currency
    };

    private async Task HandleAsync(ReturnBankDeposit command, CancellationToken ct)
    {
        using var scope = _adapter.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReturnBankDepositHandler>().HandleAsync(command, ct);
    }

    private async Task<List<BankTransfer>> TransfersAsync(Guid commandId, CancellationToken ct)
    {
        await using var db = bankDb.CreateContext();
        return await db.Transfers.AsNoTracking().Where(t => t.CommandId == commandId).ToListAsync(ct);
    }

    /// <summary>Transfer göndericinin IBAN'ına, bankaya gidiyor ve sonucu bekleniyor.</summary>
    [Fact]
    public async Task Iade_GondericininIbaninaGider_HavaleyeBagli()
    {
        var ct = TestContext.Current.CancellationToken;
        var deposit = await DepositAsync(SenderIban, ct);
        var command = Command(deposit);

        await HandleAsync(command, ct);
        await HandleAsync(command, ct);

        var transfer = (await TransfersAsync(command.CommandId, ct)).ShouldHaveSingleItem();
        transfer.Status.ShouldBe("pending");
        transfer.SagaId.ShouldBe(command.SagaId);
        transfer.DestinationIban.ShouldBe(SenderIban);
        transfer.Amount.ShouldBe(80m);
        transfer.ReturnsDepositId.ShouldBe(deposit.Id);
        transfer.BankReference.ShouldNotBeNullOrWhiteSpace();
        transfer.ReplyRoutingKey.ShouldBeNull();
    }

    /// <summary>
    /// Bildirimde IBAN yoksa ya da havale kayıtta yoksa iade gidemez: bankaya hiç gidilmeden
    /// kalıcı hata. Cevap çekimdekiyle aynı biçimde saklanıyor ve relay yayınlıyor; saga
    /// parayı askıya geri koyuyor.
    /// </summary>
    [Fact]
    public async Task IbanYokVeyaHavaleYok_BankayaGidilmeden_KaliciHata()
    {
        var ct = TestContext.Current.CancellationToken;
        var withoutIban = Command(await DepositAsync(senderIban: null, ct));
        var unknown = Command(await DepositAsync(SenderIban, ct)) with { DepositBankReference = "GLN-YOK" };

        foreach (var command in new[] { withoutIban, unknown })
        {
            await HandleAsync(command, ct);

            var transfer = (await TransfersAsync(command.CommandId, ct)).ShouldHaveSingleItem();
            transfer.Status.ShouldBe("failed");
            transfer.BankReference.ShouldBeNull();
            transfer.ResolvedAt.ShouldNotBeNull();
            transfer.ResolvedVia.ShouldBe(ReturnBankDepositHandler.ViaAdapter);
            transfer.ReplyRoutingKey.ShouldBe(nameof(BankTransferFailed));
            JsonDocument.Parse(transfer.ReplyPayload!).RootElement.GetProperty("sagaId").GetGuid().ShouldBe(command.SagaId);
        }
    }
}

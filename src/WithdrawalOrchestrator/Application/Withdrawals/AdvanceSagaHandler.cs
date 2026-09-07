using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// Wallet ve bank-service'ten gelen event'leri saga'ya uygular ve — geçiş yeni bir
/// adım gerektiriyorsa — bir sonraki komutu AYNI transaction'da outbox'a koyar
/// (decisions.md madde 32).
///
/// <b>Karar burada verilmiyor.</b> Hangi geçişin geçerli olduğunu state machine
/// söylüyor; bu sınıf yalnızca sonucu kalıcı hale getiriyor ve <c>Conflict</c>'i
/// görünür kılıyor.
///
/// <b><c>processed_messages</c> YOK.</b> Orchestrator'ın event tüketiminde ayrı bir
/// deduplikasyon tablosu gerekmiyor: saga'nın kendi durumu zaten "bu event uygulandı
/// mı" sorusunu cevaplıyor ve tekrar <c>Ignored</c> olarak dönüyor (madde 31, 32).
/// </summary>
public sealed class AdvanceSagaHandler(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<AdvanceSagaHandler> logger)
{
    public Task<TransitionResult> HandleAsync(WithdrawalDebited @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(WithdrawalDebited), (saga, now) =>
        {
            var result = saga.Debited(@event.LedgerTransactionId, @event.TotalDebited, now);

            if (result is not TransitionResult.Applied) return (result, null);

            // Bankaya giden tutar müşterinin İSTEDİĞİ tutar; komisyon bizde kalıyor
            // ve TotalDebited'ın farkı o. IBAN burada string'e dönüyor — sözleşme
            // paketine domain tipi girmiyor, ama değer zaten doğrulanmış.
            var command = OutboxMessage.For(saga.Id, commandId => new StartBankTransfer
            {
                CommandId = commandId,
                SagaId = saga.Id,
                Amount = saga.Amount,
                Currency = saga.Currency,
                DestinationIban = saga.Destination.Value
            }, now);

            // İkinci geçiş, aynı transaction'da: komut yazıldı ve saga artık banka
            // cevabını bekliyor. Ayrı commit'ler olsaydı arada çökme "komut gitti ama
            // saga hâlâ Debited" bırakırdı ve takılmış saga taraması aynı komutu
            // ikinci kez üretirdi.
            saga.BankTransferStarted(command.Id, now);

            return (result, command);
        }, ct);

    public Task<TransitionResult> HandleAsync(WithdrawalDebitRejected @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(WithdrawalDebitRejected), (saga, now) =>
            // Hiçbir para hareketi olmadı; telafi edilecek bir şey yok, saga burada bitiyor.
            (saga.Rejected(@event.Reason, now), null), ct);

    public Task<TransitionResult> HandleAsync(BankTransferSucceeded @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(BankTransferSucceeded), (saga, now) =>
        {
            var result = saga.BankTransferSucceeded(@event.BankReference, @event.FeeAmount, now);

            if (result is not TransitionResult.Applied) return (result, null);

            // Ücret TAŞINIYOR, tutar taşınmıyor: çekim tutarını wallet kendi yazdı ve
            // clearing'de duruyor, ücreti ise yalnızca banka biliyor. Bilgiyi kim
            // üretiyorsa o taşıyor — RefundWithdrawal'daki kuralın aynası.
            var command = OutboxMessage.For(saga.Id, commandId => new SettleWithdrawal
            {
                CommandId = commandId,
                SagaId = saga.Id,
                FeeAmount = @event.FeeAmount,
                BankReference = @event.BankReference
            }, now);

            return (result, command);
        }, ct);

    public Task<TransitionResult> HandleAsync(WithdrawalSettled @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(WithdrawalSettled), (saga, now) =>
            (saga.Settled(@event.LedgerTransactionId, now), null), ct);

    public Task<TransitionResult> HandleAsync(BankTransferFailed @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(BankTransferFailed), (saga, now) =>
        {
            var result = saga.BankTransferFailed(@event.Reason, now);

            if (result is not TransitionResult.Applied) return (result, null);

            // Tutar TAŞINMIYOR: ters kayıt orijinalin aynası ve orijinali wallet
            // yazdı. Buradan tutar göndermek ikinci bir doğruluk kaynağı yaratırdı.
            var command = OutboxMessage.For(saga.Id, commandId => new RefundWithdrawal
            {
                CommandId = commandId,
                SagaId = saga.Id
            }, now);

            return (result, command);
        }, ct);

    public Task<TransitionResult> HandleAsync(WithdrawalRefunded @event, CancellationToken ct) =>
        ApplyAsync(@event.SagaId, nameof(WithdrawalRefunded), (saga, now) =>
            (saga.Refunded(@event.LedgerTransactionId, now), null), ct);

    /// <param name="step">
    /// Geçişin tamamı: state machine'i çağırır ve gerekiyorsa bir sonraki komutu
    /// üretir. Tek delege çünkü ikisi ayrılamaz — komutun kimliği saga'ya yazılıyor.
    /// </param>
    private async Task<TransitionResult> ApplyAsync(
        Guid sagaId,
        string eventName,
        Func<WithdrawalSaga, DateTimeOffset, (TransitionResult Result, OutboxMessage? Command)> step,
        CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var saga = await db.Sagas.FirstOrDefaultAsync(s => s.Id == sagaId, ct)
                   ?? throw new SagaNotFoundException(sagaId);

        var (result, command) = step(saga, timeProvider.GetUtcNow());

        switch (result)
        {
            case TransitionResult.Ignored:
                // Broker en az bir kez teslim ediyor; tekrar BEKLENEN durum.
                // Hiçbir şey yazılmıyor, saga'nın zaman damgası bile dokunmuyor.
                logger.LogDebug(
                    "{Event} yok sayıldı, saga zaten {State}. Saga {SagaId}",
                    eventName, saga.State, sagaId);

                return result;

            case TransitionResult.Conflict:
                // Alarm. Yok saymak zararı görünmez kılardı: telafiden sonra gelen
                // "başarılı" gibi bir çelişki para hem bankadan çıkmış hem müşteriye
                // iade edilmiş olabileceği anlamına geliyor (decisions.md madde 31).
                logger.LogError(
                    "{Event} saga durumuyla ÇELİŞİYOR. Saga {SagaId} durumu {State}, işlem yapılmadı.",
                    eventName, sagaId, saga.State);

                return result;
        }

        if (command is not null)
        {
            db.Outbox.Add(command);
        }

        await db.SaveChangesAsync(ct);

        return result;
    }
}

/// <summary>
/// Event geldi ama saga yok. Beklenen bir durum DEĞİL — mesaj dead-letter'a gidiyor,
/// yeniden denemek aynı sonucu verir.
/// </summary>
public sealed class SagaNotFoundException(Guid sagaId)
    : Exception($"Saga bulunamadı: {sagaId}")
{
    public Guid SagaId { get; } = sagaId;
}

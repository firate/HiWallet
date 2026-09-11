using HiWallet.Shared.Contracts.Withdrawals;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.WithdrawalOrchestrator.Application.Withdrawals;

/// <summary>
/// Saga'yı başlatır ve ilk komutu (<see cref="DebitForWithdrawal"/>) outbox'a koyar.
/// İkisi AYNI commit'te (decisions.md madde 32).
///
/// Bu noktada hiçbir para hareket etmedi ve wallet'a hiçbir şey sorulmadı: bakiye,
/// limit ve komisyon wallet'ın bilgisi. Orchestrator yalnızca niyeti kalıcı hale
/// getiriyor.
/// </summary>
public sealed class StartWithdrawalHandler(
    IDbContextFactory<OrchestratorDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<StartWithdrawalHandler> logger)
{
    public async Task<StartWithdrawalResult> HandleAsync(
        StartWithdrawalCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var saga = WithdrawalSaga.Start(
            id: Guid.NewGuid(),
            accountId: command.AccountId,
            walletId: command.WalletId,
            amount: command.Amount,
            currency: command.Currency,
            destination: command.Destination,
            idempotencyKey: command.IdempotencyKey,
            initiatedBy: command.InitiatedBy,
            startedAt: now);

        // Komisyon TAŞINMIYOR: politikayı wallet uyguluyor ve iki serviste
        // tekrarlanırsa ilk sapmada sessizce ayrışır.
        var message = OutboxMessage.For(
            saga.Id,
            commandId => new DebitForWithdrawal
            {
                CommandId = commandId,
                SagaId = saga.Id,
                WalletId = saga.WalletId,
                Amount = saga.Amount,
                Currency = saga.Currency,
                Actor = saga.InitiatedBy()
            },
            now);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        db.Sagas.Add(saga);
        db.Outbox.Add(message);

        try
        {
            // Tek SaveChanges = tek transaction; EF ikisini birlikte yazıyor.
            // Ayrıca BeginTransaction'a gerek yok.
            await db.SaveChangesAsync(ct);

            return new StartWithdrawalResult(saga.Id, saga.State, Replayed: false);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            // "Önce SELECT sonra INSERT" YOK (CLAUDE.md "Idempotency"): tekilliğe
            // veritabanı karar veriyor, uygulama değil. Kontrolü önce yapsaydık iki
            // eşzamanlı istek arasında TOCTOU açığı kalırdı ve müşteri iki kez
            // para çekerdi.
            //
            // Çakışan commit tamamen geri alındı — outbox satırı da yazılmadı.
            // Bu şart: yazılsaydı wallet'a ikinci bir düşme komutu giderdi.
            logger.LogInformation(
                "Çekim isteği tekrar; mevcut saga dönülüyor. Hesap {AccountId}, anahtar {Key}",
                command.AccountId, command.IdempotencyKey);
        }

        // Başarısız commit context'i kullanılamaz hale getiriyor; taze bir tane açılıyor.
        await using var read = await contextFactory.CreateDbContextAsync(ct);

        var existing = await read.Sagas
            .AsNoTracking()
            .SingleAsync(
                s => s.AccountId == command.AccountId && s.IdempotencyKey == command.IdempotencyKey,
                ct);

        return new StartWithdrawalResult(existing.Id, existing.State, Replayed: true);
    }

    /// <summary>
    /// Yalnızca idempotency ihlali yutuluyor. Başka bir unique ihlali ya da FK hatası
    /// gerçek bir arıza ve yukarı çıkmalı — hepsini "tekrar" saymak bozuk veriyi
    /// başarı gibi gösterirdi.
    /// </summary>
    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            postgres
        && postgres.ConstraintName == "ux_withdrawal_sagas_idempotency";
}

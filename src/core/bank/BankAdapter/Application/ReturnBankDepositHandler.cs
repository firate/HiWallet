using HiWallet.BankIntegration.Domain;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Contracts.DepositReturns;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Askıdaki havaleyi göndericiye geri gönderir. Gönderenin IBAN'ı komutta YOK: havale
/// kendi kaydında (<c>bank_deposits</c>) bankanın referansıyla bulunuyor ve IBAN oradan
/// okunuyor; banka entegrasyonunun dışına çıkmıyor.
///
/// Gerisi çekimin transferiyle aynı (<see cref="StartBankTransferHandler"/>): önce banka,
/// sonra kayıt; cevap üretilmiyor, sonuç callback ya da mutabakatla geliyor. Satır iade
/// edilen havaleye bağlı.
///
/// <b>İade edilemeyen havale.</b> Bildiriminde IBAN yoksa, havale kayıtta yoksa ya da tutar
/// tutmuyorsa banka hiç aranmıyor: satır kalıcı hatayla kapanıyor ve cevap çekimdeki gibi
/// saklanıyor. Relay yayınlıyor, saga parayı askıya geri koyuyor.
/// </summary>
internal sealed class ReturnBankDepositHandler(
    IDbContextFactory<BankDbContext> contextFactory,
    BankClient bank,
    TimeProvider timeProvider,
    ILogger<ReturnBankDepositHandler> logger)
{
    /// <summary>Sonucu adaptörün kendisi verdi; banka aranmadı.</summary>
    public const string ViaAdapter = "adapter";

    public async Task HandleAsync(ReturnBankDeposit command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        if (await db.Transfers.AnyAsync(t => t.CommandId == command.CommandId, ct))
        {
            // Tekrar teslim: transfer tekrarlanmıyor, cevabı relay'in işi.
            logger.LogInformation("İade komutu zaten işlenmiş. Saga {SagaId}", command.SagaId);
            return;
        }

        var deposit = await db.Deposits
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Provider == command.Provider && d.BankReference == command.DepositBankReference, ct);

        var unreturnable = deposit switch
        {
            null => "İade edilecek havale banka kaydında yok.",
            { SenderIban: null or "" } => "Havalenin bildiriminde gönderenin IBAN'ı yok; iade edilemez.",
            _ when deposit.Amount != command.Amount || deposit.Currency != command.Currency =>
                "İade tutarı havalenin tutarıyla tutmuyor.",
            _ => null
        };

        if (unreturnable is not null)
        {
            await RecordUnreturnableAsync(db, command, deposit, unreturnable, ct);
            return;
        }

        var iban = deposit!.SenderIban!;

        // ÖNCE BANKA, SONRA KAYIT; gerekçe StartBankTransferHandler'da.
        var accepted = await bank.StartTransferAsync(
            new StartTransferRequest(command.SagaId.ToString(), command.Amount, command.Currency, iban),
            command.CommandId,
            ct);

        db.Transfers.Add(new BankTransfer
        {
            CommandId = command.CommandId,
            SagaId = command.SagaId,
            Amount = command.Amount,
            Currency = command.Currency,
            DestinationIban = iban,
            ReturnsDepositId = deposit.Id,
            Status = BankTransferStatus.Pending.ToText(),
            BankReference = accepted.BankReference,
            StartedAt = timeProvider.GetUtcNow()
        });

        if (!await SaveAsync(db, command, ct))
        {
            return;
        }

        logger.LogInformation(
            "Havale iadesi bankaya iletildi. Saga {SagaId}, havale {DepositReference} → {BankReference}, " +
            "{Amount} {Currency} → {Iban}",
            command.SagaId, command.DepositBankReference, accepted.BankReference, command.Amount, command.Currency,
            StartBankTransferHandler.Mask(iban));
    }

    private async Task RecordUnreturnableAsync(
        BankDbContext db, ReturnBankDeposit command, BankDeposit? deposit, string reason, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var transfer = new BankTransfer
        {
            CommandId = command.CommandId,
            SagaId = command.SagaId,
            Amount = command.Amount,
            Currency = command.Currency,
            DestinationIban = null,
            ReturnsDepositId = deposit?.Id,
            Status = BankTransferStatus.Failed.ToText(),
            FailureReason = reason,
            StartedAt = now,
            ResolvedAt = now,
            ResolvedVia = ViaAdapter
        };

        (transfer.ReplyRoutingKey, transfer.ReplyPayload) = TransferCompleter.BuildReply(transfer, BankTransferStatus.Failed);

        db.Transfers.Add(transfer);

        if (await SaveAsync(db, command, ct))
        {
            // Tutar tutmuyorsa wallet ile banka ayrışmış: para askıya dönüyor ama sebep
            // incelenmeli.
            logger.LogError(
                "Havale iade edilemedi, banka aranmadı. Saga {SagaId}, havale {DepositReference}: {Reason}",
                command.SagaId, command.DepositBankReference, reason);
        }
    }

    /// <returns><c>false</c>: aynı komut paralel işlendi ve öbür taraf önce yazdı.</returns>
    private async Task<bool> SaveAsync(BankDbContext db, ReturnBankDeposit command, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("İade komutu paralel işlendi. Saga {SagaId}", command.SagaId);
            return false;
        }
    }
}

using System.Text.Json;
using HiWallet.BankIntegration.Domain;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Contracts.Withdrawals;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Bir transferi KAPATAN tek kod. İki yol buraya varıyor — bankanın callback'i ve
/// mutabakat taraması — ve ikisi de aynı işi yapmak zorunda (decisions.md madde 35).
///
/// <b>İki kopya olsaydı</b> callback yolu ile tarama yolu zamanla ayrışırdı: biri
/// ücreti yazar öbürü yazmaz, biri cevabı saklar öbürü doğrudan yayınlar. Ve bu
/// ayrışma ancak callback'i kaçırılmış bir transferde, yani zaten seyrek bir yolda
/// görünürdü.
///
/// <b>Cevap YAYINLANMIYOR, SAKLANIYOR.</b> Yayın <see cref="Infrastructure.Messaging.ReplyRelay"/>'in
/// işi. Burada yayınlansaydı "sonucu öğrendim" ile "cevabı yayınladım" aynı ana
/// bağlanırdı ve broker erişilemezken sonuç ya kaybolur ya da transfer kapanmamış
/// sayılırdı.
/// </summary>
internal sealed class TransferCompleter(
    IDbContextFactory<BankDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<TransferCompleter> logger)
{
    /// <summary>Sonucu callback getirdi.</summary>
    public const string ViaCallback = "callback";

    /// <summary>Sonucu mutabakat taraması getirdi. Bu değerin artması alarm konusu.</summary>
    public const string ViaReconciliation = "reconciliation";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Transferi kapatır ve cevabını saklar. Zaten kapanmışsa hiçbir şey yapmadan
    /// <c>false</c> dönüyor — iki yol aynı sonucu getirdiğinde (callback geç geldi,
    /// tarama önce buldu) ikincisi zararsız.
    /// </summary>
    /// <param name="bankReference">
    /// Kapatılacak transfer bunun üzerinden bulunuyor. Callback bankanın referansını
    /// taşıyor; tarama zaten o referansla sordu.
    /// </param>
    public async Task<bool> ResolveAsync(
        string bankReference,
        BankTransferStatus status,
        decimal fee,
        string? failureReason,
        string via,
        CancellationToken ct)
    {
        if (!status.IsTerminal())
        {
            throw new ArgumentException(
                "Yalnızca terminal durum transferi kapatabilir; 'pending' ile çağrılmamalı.",
                nameof(status));
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var transfer = await db.Transfers
            .FirstOrDefaultAsync(t => t.BankReference == bankReference, ct);

        if (transfer is null)
        {
            // Bankanın bilmediğimiz bir referans için gönderdiği bildirim. Kaydı
            // olmayan bir transferi kapatamayız ve UYDURAMAYIZ: saga'ya cevap
            // üretmek, hiç göndermediğimiz bir transferi tamamlanmış göstermek olurdu.
            logger.LogError(
                "Tanınmayan banka referansı için sonuç geldi: {BankReference}. " +
                "Ya kayıt yazılamadı ya da bildirim başka bir kuruluma ait.",
                bankReference);

            return false;
        }

        if (transfer.ResolvedAt is not null)
        {
            logger.LogDebug(
                "Transfer zaten kapanmış, sonuç yok sayılıyor. {BankReference} ({Via})",
                bankReference, via);

            return false;
        }

        var now = timeProvider.GetUtcNow();

        transfer.Status = status.ToText();
        transfer.Fee = fee;
        transfer.FailureReason = status is BankTransferStatus.Failed ? failureReason : null;
        transfer.ResolvedAt = now;
        transfer.ResolvedVia = via;

        // Cevap BURADA üretiliyor, yayın anında değil: yayın tekrarlandığında
        // gövdenin birebir aynı kalması gerekiyor. Yayın anında yeniden üretilseydi
        // araya giren her değişiklik iki farklı mesaj üretirdi.
        var (routingKey, payload) = BuildReply(transfer, status);

        transfer.ReplyRoutingKey = routingKey;
        transfer.ReplyPayload = payload;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Transfer kapandı. {BankReference} → {Status} ({Via}), saga {SagaId}",
            bankReference, transfer.Status, via, transfer.SagaId);

        return true;
    }

    private static (string RoutingKey, string Payload) BuildReply(
        BankTransfer transfer, BankTransferStatus status)
    {
        if (status is BankTransferStatus.Succeeded)
        {
            var succeeded = new BankTransferSucceeded
            {
                SagaId = transfer.SagaId,
                BankReference = transfer.BankReference!,
                FeeAmount = transfer.Fee ?? 0m
            };

            return (nameof(BankTransferSucceeded),
                JsonSerializer.Serialize(succeeded, JsonOptions));
        }

        var failed = new BankTransferFailed
        {
            SagaId = transfer.SagaId,
            Reason = transfer.FailureReason ?? "Banka transferi reddetti."
        };

        return (nameof(BankTransferFailed), JsonSerializer.Serialize(failed, JsonOptions));
    }
}

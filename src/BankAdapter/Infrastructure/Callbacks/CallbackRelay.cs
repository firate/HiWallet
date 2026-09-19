using System.Text.Json;
using HiWallet.BankAdapter.Application;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankAdapter.Infrastructure.Callbacks;

/// <summary>
/// <c>bank_callbacks</c> inbox'ındaki işlenmemiş bildirimleri alıp transferleri
/// kapatır. <b>Sonucun ASIL geldiği yol bu</b> (decisions.md madde 35).
///
/// <b>Neden burada, <c>bank-webhook</c>'un içinde değil.</b> İki sebep ve ikisi de
/// <c>topup-webhook</c>'un şeklinden bilinçli sapmayı gerektiriyor:
/// <list type="number">
/// <item>Callback yolu ile mutabakat taraması AYNI kapanış koduna varmak zorunda
/// (<see cref="TransferCompleter"/>) ve o kodun tek kopyası olmalı.</item>
/// <item>İşleme mantığındaki her değişiklik aksi halde bankanın çağırdığı endpoint'i
/// yeniden başlatmayı gerektirirdi. Adaptör yeniden başlarken kayıp yok; callback
/// alıcısı yeniden başlarken banka bağlantı hatası alıyor.</item>
/// </list>
///
/// <b>Gövde burada çözülüyor, webhook'ta değil.</b> Webhook'un tek işi doğrula,
/// yaz, <c>202</c>. Çözümleme hatası bankaya <c>400</c> olarak dönseydi banka
/// tekrar denemez ve sonuç kaybolurdu; burada ise satır işlenmemiş kalıyor ve
/// alarm konusu oluyor.
/// </summary>
internal sealed class CallbackRelay(
    IDbContextFactory<BankDbContext> contextFactory,
    TransferCompleter completer,
    JobLease lease,
    TimeProvider timeProvider,
    ILogger<CallbackRelay> logger) : BackgroundService
{
    private const string JobName = "bank:callback-relay";

    private const int BatchSize = 50;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = 0;

                var ran = await lease.TryRunAsync(
                    JobName,
                    async ct => processed = await ProcessBatchAsync(ct),
                    stoppingToken);

                if (!ran || processed < BatchSize)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception, "Callback relay turu başarısız, {Delay} sonra yeniden denenecek.", ErrorDelay);

                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var pending = await db.Callbacks
            .FromSql($"""
                      SELECT * FROM bank_callbacks
                      WHERE processed_at IS NULL
                      ORDER BY received_at
                      LIMIT {BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var processed = 0;

        foreach (var callback in pending)
        {
            ct.ThrowIfCancellationRequested();

            callback.ProcessAttempts++;

            try
            {
                await ApplyAsync(callback, ct);

                callback.ProcessedAt = timeProvider.GetUtcNow();
                callback.LastError = null;

                processed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Satır işlenmemiş kalıyor ve bir sonraki turda yeniden deneniyor.
                // İşlenmemiş kalan callback'ler alarm konusu: bankanın gönderdiği
                // bir sonucu işleyemiyoruz demek.
                callback.LastError = exception.Message;

                logger.LogError(
                    exception, "Callback işlenemedi. {Provider}/{EventId}, deneme {Attempt}",
                    callback.Provider, callback.EventId, callback.ProcessAttempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return processed;
    }

    private async Task ApplyAsync(BankCallback callback, CancellationToken ct)
    {
        var notification = JsonSerializer.Deserialize<CallbackNotification>(callback.RawPayload, JsonOptions)
                           ?? throw new JsonException("Callback gövdesi boş.");

        var status = BankClient.Map(notification.Status);

        if (status is null)
        {
            // Bankanın tanımadığımız bir durum kelimesi. Tahmin ETMİYORUZ: yanlış
            // tahmin ya müşterinin parasını gereksiz geri gönderir ya da hiç
            // gitmemiş parayı gitmiş gösterir.
            throw new InvalidOperationException(
                $"Bankanın bilinmeyen durumu: '{notification.Status}'. Sözleşme değişmiş olabilir.");
        }

        if (status is BankIntegration.Domain.BankTransferStatus.Pending)
        {
            // Banka "hâlâ bekliyor" diye callback göndermiş. İşlenmiş sayılıyor —
            // kapatacak bir şey yok ve satırı açık bırakmak alarmı kirletirdi.
            logger.LogInformation(
                "Bekliyor bildirimi, kapatılacak bir şey yok. {BankReference}", notification.BankReference);

            return;
        }

        await completer.ResolveAsync(
            notification.BankReference,
            status.Value,
            notification.Fee,
            notification.FailureReason,
            TransferCompleter.ViaCallback,
            ct);
    }
}

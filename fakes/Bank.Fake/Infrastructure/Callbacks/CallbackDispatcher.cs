using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HiWallet.Bank.Fake.Application;
using HiWallet.Bank.Fake.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace HiWallet.Bank.Fake.Infrastructure.Callbacks;

/// <summary>
/// Sonucu belli olmuş transferler için bize callback gönderir.
///
/// <b>Bu sahte bankanın "asıl yol" tarafı.</b> Gerçek entegrasyonda sonuçların
/// neredeyse tamamı buradan gelir; mutabakat taraması yalnızca buranın kaçırdığını
/// toplar (decisions.md madde 35).
///
/// <b>Sonsuza kadar denemiyor.</b> <see cref="CallbackOptions.MaxAttempts"/> dolunca
/// vazgeçiyor ve transfer callback'siz kalıyor. Bu bir eksiklik değil, sahte bankanın
/// en değerli davranışlarından biri: bizim tarafta mutabakat taramasının neden
/// zorunlu olduğunu kanıtlayan senaryo tam olarak bu.
///
/// <b>Tekillik kilidi YOK.</b> Sahte banka tek instance koşuyor ve hafızası zaten
/// process'e ait. Bizim tarafımızdaki relay ve taramalar <c>pg_try_advisory_lock</c>
/// kullanıyor; burada aynı şeyi kurmak taklit edilen tarafa bizim mühendisliğimizi
/// yüklemek olurdu. HTTP çağrısı depo kilidinin DIŞINDA yapılıyor: yavaş bir endpoint
/// bankanın transfer kabulünü bekletmemeli.
/// </summary>
internal sealed class CallbackDispatcher(
    BankFakeStore store,
    IHttpClientFactory httpClientFactory,
    IOptions<BankFakeOptions> options,
    TimeProvider timeProvider,
    ILogger<CallbackDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "bank-callback";

    private const int BatchSize = 20;

    private readonly CallbackOptions _callback = options.Value.Callback;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_callback.Enabled)
        {
            // Durum sorgusu olan ama callback göndermeyen bankaları temsil ediyor.
            // Bizim tarafta o kurulumda sonuç yalnızca mutabakat taramasıyla
            // öğreniliyor ve taramanın aralığı doğrudan müşterinin bekleme süresi.
            logger.LogInformation("Callback kapalı; sonuçlar yalnızca durum sorgusuyla öğrenilecek.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_callback.Url) || string.IsNullOrWhiteSpace(_callback.Secret))
        {
            throw new InvalidOperationException(
                "BankFake:Callback:Enabled açık ama Url ya da Secret verilmemiş. " +
                "İmzasız callback gönderen bir banka, doğrulayıcımızın hiç sınanmaması demek olurdu.");
        }

        using var timer = new PeriodicTimer(_callback.PollInterval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Tur hatası göndericiyi öldürmüyor. Öldürseydi sahte banka sessizce
                // susar ve testler "neden hiç callback gelmiyor" diye aranırdı.
                logger.LogWarning(exception, "Callback turu başarısız, sonraki turda yeniden denenecek.");
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        List<BankTransfer> due;

        lock (store.Gate)
        {
            due = store.TransfersByReference.Values
                .Where(t => t.CallbackSentAt == null
                            && t.ResolveAt <= now
                            && t.CallbackAttempts < _callback.MaxAttempts)
                .OrderBy(t => t.ResolveAt)
                .Take(BatchSize)
                .ToList();
        }

        foreach (var transfer in due)
        {
            ct.ThrowIfCancellationRequested();

            var status = TransferResolution.StatusOf(transfer, now);

            // Sonucu hâlâ belirsiz olan bir transfer buraya düşmemeli; düştüyse
            // ResolveAt filtresi ile durum türetmesi ayrışmış demektir.
            if (status is TransferStatus.Pending) continue;

            string? error = null;

            try
            {
                await SendAsync(transfer, status, now, ct);

                logger.LogInformation(
                    "Callback gönderildi. {BankReference} → {Status}", transfer.BankReference, status);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
            }

            int attempts;

            // Sayaç her denemede artıyor, başarılı ya da değil. Artmasaydı
            // "vazgeçme" davranışı hiç gerçekleşmez ve gönderici sonsuza kadar denerdi.
            lock (store.Gate)
            {
                attempts = ++transfer.CallbackAttempts;
                transfer.LastCallbackError = error;

                if (error is null)
                {
                    transfer.CallbackSentAt = timeProvider.GetUtcNow();
                }
            }

            if (error is not null)
            {
                logger.LogWarning(
                    "Callback gönderilemedi. {BankReference}, deneme {Attempt}/{Max}: {Error}",
                    transfer.BankReference, attempts, _callback.MaxAttempts, error);
            }
        }
    }

    private async Task SendAsync(
        BankTransfer transfer, string status, DateTimeOffset now, CancellationToken ct)
    {
        var notification = new CallbackNotification
        {
            // Referanstan türetiliyor: tekrar denemelerde AYNI kalmak zorunda, yoksa
            // bizim inbox'ımız aynı sonucu iki ayrı event sanar.
            EventId = $"evt-{transfer.BankReference}",
            BankReference = transfer.BankReference,
            ClientReference = transfer.ClientReference,
            Status = status,
            Fee = transfer.Fee,
            Currency = transfer.Currency,
            FailureReason = status is TransferStatus.Failed ? TransferResolution.FailureReason : null,
            OccurredAt = now
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(notification, JsonOptions);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, _callback.Url);
        request.Content = content;

        // İmza gönderilen TAM baytlar üzerinde. Nesneyi yeniden serialize edip
        // imzalasaydık boşluk ve alan sırası farkı doğrulamayı düşürürdü.
        request.Headers.TryAddWithoutValidation(
            BankCallbackSignature.HeaderName,
            BankCallbackSignature.Compute(body, _callback.Secret!));

        var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.SendAsync(request, ct);

        // 2xx dışındaki her şey başarısız sayılıyor — 4xx dahil. Gerçek banka da
        // imzayı reddeden bir endpoint'le karşılaştığında "gönderdim" diye işaretlemez.
        response.EnsureSuccessStatusCode();
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

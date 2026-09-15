using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HiWallet.BankIntegration.Domain;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Bankayla konuşan tek yer. Dışarıya çağrı yapan kodun tamamı burada toplanıyor;
/// handler ve mutabakat taraması HTTP bilmiyor.
///
/// <b>Geçici hata ile kalıcı hata burada ayrılıyor</b> ve bu ayrım bütün akışın
/// en kritik noktası: kalıcı hata saga'yı telafiye sokuyor (müşterinin parası geri
/// gidiyor), geçici hata hiçbir şey bildirmeden yeniden deneniyor. Karıştırılırsa
/// her ağ kesintisi müşterinin parasını ileri geri taşır (overview.md madde 6).
///
/// Ölçüt: <b>bankanın verdiği kalıcı cevap</b> kalıcı hata, <b>cevap alamamak</b>
/// geçici hata. 5xx, timeout ve bağlantı hatası geçici; transferin <c>failed</c>
/// dönmesi kalıcı.
/// </summary>
internal sealed class BankClient(
    IHttpClientFactory httpClientFactory, ILogger<BankClient> logger)
{
    public const string HttpClientName = "bank";

    /// <remarks>
    /// Tipli istemci yerine adlandırılmış istemci: bu sınıf singleton olarak
    /// kaydediliyor (arka plan servisleri kullanıyor) ve tipli istemci transient
    /// olurdu. Singleton'ın yakaladığı bir <c>HttpClient</c> handler rotasyonunu
    /// kaçırır, yani DNS değişikliğini uzun süre görmez.
    /// </remarks>
    private HttpClient Client => httpClientFactory.CreateClient(HttpClientName);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Transferi başlatır. Dönen şey SONUÇ DEĞİL, bankanın referansı: sonuç sonra
    /// callback ya da durum sorgusuyla öğreniliyor (decisions.md madde 35).
    /// </summary>
    /// <param name="idempotencyKey">
    /// Komut kimliği. Aynı anahtarla ikinci istek bankada YENİ transfer açmıyor —
    /// süreç bankayı arayıp kaydı yazmadan ölse bile tekrar teslimde aynı transferi
    /// geri alıyoruz.
    /// </param>
    /// <exception cref="TransientBankException">Banka cevap vermedi; yeniden denenmeli.</exception>
    public async Task<StartTransferResponse> StartTransferAsync(
        StartTransferRequest request, Guid idempotencyKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/transfers")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString());

        HttpResponseMessage response;

        try
        {
            response = await Client.SendAsync(message, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
        {
            // Bankaya HİÇ ULAŞAMADIK. Transfer başlamış olabilir de olmayabilir de —
            // ve bu belirsizlik tam olarak idempotency anahtarının çözdüğü şey.
            throw new TransientBankException("Bankaya ulaşılamadı.", exception);
        }

        using (response)
        {
            // 5xx ve 429: banka "şu an olmaz" diyor. Kalıcı bir cevap değil.
            if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                throw new TransientBankException(
                    $"Banka {(int)response.StatusCode} döndü.", innerException: null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 4xx: isteğimiz bozuk. Yeniden denemek aynı sonucu verir; bu bir
                // KOD hatası ve dead-letter'a gitmeli, sonsuz döngüye değil.
                var body = await response.Content.ReadAsStringAsync(ct);

                throw new PermanentBankException(
                    $"Banka isteği reddetti ({(int)response.StatusCode}): {body}");
            }

            var accepted = await response.Content.ReadFromJsonAsync<StartTransferResponse>(JsonOptions, ct)
                           ?? throw new PermanentBankException("Banka boş gövde döndü.");

            logger.LogInformation(
                "Banka transferi kabul etti. {BankReference}, replayed={Replayed}",
                accepted.BankReference, accepted.Replayed);

            return accepted;
        }
    }

    /// <summary>
    /// Transferin o anki durumu. Mutabakat taramasının kullandığı uç.
    ///
    /// Banka referansı tanımıyorsa <c>null</c> dönüyor — hata DEĞİL: kabul edilmiş
    /// ama bizim kaydımızda referansı olmayan bir transfer olabilir ve tarama bunu
    /// atlayıp devam etmeli.
    /// </summary>
    /// <exception cref="TransientBankException">Banka cevap vermedi; sonraki turda yeniden.</exception>
    public async Task<TransferStatusResponse?> GetStatusAsync(string bankReference, CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await Client.GetAsync($"v1/transfers/{Uri.EscapeDataString(bankReference)}", ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !ct.IsCancellationRequested)
        {
            throw new TransientBankException("Bankaya ulaşılamadı.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound) return null;

            if (!response.IsSuccessStatusCode)
            {
                throw new TransientBankException(
                    $"Durum sorgusu {(int)response.StatusCode} döndü.", innerException: null);
            }

            return await response.Content.ReadFromJsonAsync<TransferStatusResponse>(JsonOptions, ct);
        }
    }

    /// <summary>
    /// Bankanın durum kelimesini bizim durumumuza çevirir. TEK YERDE: sözleşme
    /// değişirse tek bir noktada kırılsın.
    ///
    /// Tanınmayan bir kelime <c>null</c> dönüyor, istisna değil — bilinmeyen durumu
    /// "başarısız" saymak müşterinin parasını gereksiz yere geri gönderirdi,
    /// "başarılı" saymak ise hiç gitmemiş parayı gitmiş gösterirdi. İkisi de yanlış;
    /// doğrusu kararı ERTELEMEK ve transferi bekleyen olarak bırakmak.
    /// </summary>
    public static BankTransferStatus? Map(string status) => status switch
    {
        BankApiStatus.Succeeded => BankTransferStatus.Succeeded,
        BankApiStatus.Failed => BankTransferStatus.Failed,
        BankApiStatus.Pending => BankTransferStatus.Pending,
        _ => null
    };
}

/// <summary>
/// Banka cevap vermedi. Cevap ÜRETİLMİYOR: saga
/// <c>bank_transfer_pending</c>'de bekliyor ve işlem yeniden deneniyor.
/// </summary>
internal sealed class TransientBankException(string message, Exception? innerException)
    : Exception(message, innerException);

/// <summary>
/// Bankanın kalıcı reddi ya da bizim bozuk isteğimiz. Yeniden denemek aynı sonucu
/// verir; mesaj dead-letter'a gidiyor ve insan bakıyor.
/// </summary>
internal sealed class PermanentBankException(string message) : Exception(message);

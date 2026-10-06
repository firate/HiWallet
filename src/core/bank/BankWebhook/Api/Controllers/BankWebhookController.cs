using System.Text;
using System.Text.Json;
using HiWallet.BankWebhook.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.BankWebhook.Api.Controllers;

/// <summary>
/// Bankanın transfer sonucu bildirimi. <b>Bu servisin TEK endpoint'i ve tek işi:</b>
/// doğrula, inbox'a yaz, <c>202</c> (decisions.md madde 35).
///
/// İşleme, transferin kapatılması ve cevabın yayınlanması <c>bank-adapter</c>'da.
/// <c>topup-webhook</c>'ta relay webhook'un içinde koşuyor; burada bilinçli olarak
/// sapılıyor — yayın mantığındaki her değişiklik aksi halde bankanın çağırdığı endpoint'i
/// yeniden başlatmayı gerektirirdi.
/// </summary>
[ApiController]
[Route("v1/webhooks/bank")]
public sealed class BankWebhookController(
    BankSecrets secrets,
    BankCallbackWriter inbox,
    ILogger<BankWebhookController> logger) : ControllerBase
{
    /// <summary>
    /// Gövde sınırı. İmza gövdenin tamamı üzerinde hesaplandığı için sınırsız gövde
    /// doğrudan CPU tüketimi demek; kimliği doğrulanmamış bir endpoint'te bu bir saldırı yüzeyi.
    /// </summary>
    private const int MaxBodyBytes = 32 * 1024;

    /// <summary>
    /// Sıra: imza → <c>event_id</c> çıkar → inbox → <c>202</c>.
    ///
    /// <b>İmza en başta</b>, parse'tan bile önce: doğrulanmamış gövdeyi işlemeye
    /// başlamak saldırganın kontrolündeki veriyi işlemeye başlamak olurdu.
    ///
    /// <b>Response en sonda</b>, commit'ten sonra: bankaya "aldım" demek "kaybetmem"
    /// sözü vermektir ve banka sınırlı sayıda deneyip vazgeçiyor.
    ///
    /// <b>Neden <c>202</c>.</b> Verilen söz "işledim" değil "kalıcı olarak
    /// kaydettim". Saga bu response döndüğünde henüz ilerlemedi; transferi kapatan kod
    /// başka bir serviste.
    /// </summary>
    [HttpPost("{bank}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(string bank, CancellationToken ct)
    {
        // Tanınmayan kurum da 401: 404 dönmek hangi bankalarla çalıştığımızı
        // dışarıya söylerdi.
        if (!secrets.TryGet(bank, out var bankSecrets))
        {
            logger.LogWarning("Tanınmayan kurumdan callback: {Bank}", bank);
            return Unauthorized();
        }

        var body = await ReadBodyAsync(ct);

        var match = BankCallbackSignature.Match(
            body, bankSecrets, Request.Headers[BankCallbackSignature.HeaderName]);

        if (match == BankCallbackSignature.NoMatch)
        {
            logger.LogWarning("Geçersiz callback imzası. Kurum {Bank}", bank);
            return Unauthorized();
        }

        // Rotasyon uyuşmazlığındaki 401 ile saldırganın aldığı 401 log'da aynı
        // görünüyor. Eski secret'la doğrulanan her callback, karşı tarafın geçişi
        // tamamlamadığının kanıtı: bu kayıt kesilmeden eski secret kaldırılmamalı.
        if (match > 0)
        {
            logger.LogWarning(
                "Callback eski secret ile doğrulandı. Kurum {Bank}, secret sırası {Index}", bank, match);
        }

        // Gövdenin TAMAMI çözümlenmiyor; yalnızca idempotency için gereken event
        // kimliği okunuyor. Geri kalanı adaptörün işi — bozuk bir alan yüzünden
        // bankaya 400 dönmek, o sonucu kalıcı olarak kaybetmek demek olurdu.
        var eventId = TryReadEventId(body);

        if (string.IsNullOrWhiteSpace(eventId))
        {
            // Bu ayrı: event kimliği olmayan bir bildirim deduplike EDİLEMEZ.
            // Kabul etseydik bankanın tekrarı ikinci kez işlenirdi.
            logger.LogWarning("Callback'te eventId yok. Kurum {Bank}", bank);

            return Problem(
                title: "eventId zorunlu.",
                detail: "Kimliksiz bildirim tekrar gönderildiğinde ayırt edilemez.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var duplicate = await inbox.WriteAsync(bank, eventId, Encoding.UTF8.GetString(body), ct);

        if (duplicate)
        {
            logger.LogInformation(
                "Tekrar eden callback, inbox'a yazılmadı. {Bank}/{EventId}", bank, eventId);
        }

        // Tekrar da başarı: bankanın yeniden göndermesi beklenen bir davranış.
        // Hata dönmek bankayı gereksiz tekrara sokardı.
        return Accepted(new { accepted = true, duplicate });
    }

    /// <summary>
    /// Yalnızca <c>eventId</c> alanını okur. Bozuk JSON'da <c>null</c> dönüyor;
    /// istisna fırlatmıyor çünkü girdi tamamen dış dünyadan geliyor.
    /// </summary>
    private static string? TryReadEventId(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());

            return document.RootElement.TryGetProperty("eventId", out var element)
                   && element.ValueKind is JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        // Gövde ham bayt olarak okunuyor: imza tam olarak gelen baytlar üzerinde.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);

        return buffer.ToArray();
    }
}

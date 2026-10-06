using System.Globalization;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using HiWallet.Stripe.Fake.Payments;
using HiWallet.Stripe.Fake.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Stripe.Fake.Api.Controllers;

/// <summary>
/// Müşterinin kartını girdiği sayfanın yerinde duran sayfa. Gerçek sağlayıcıda kart
/// numarası ve 3D Secure burada; sahtede yalnızca "Öde" ve "Vazgeç" var. Karardan sonra
/// müşteri ödemeyi açanın verdiği adrese dönüyor, sonuç webhook'la ayrıca bildiriliyor.
/// </summary>
[Route("odeme")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CheckoutController(
    PaymentStore payments,
    Channel<PaymentWebhook> webhooks,
    TimeProvider time) : Controller
{
    /// <summary>
    /// Tutar Türkçe yazımla: binlik nokta, ondalık virgül. Kültür nesnesi kullanılamıyor,
    /// uygulamalar kültürsüz (invariant) derleniyor; biçim burada elle kuruluyor.
    /// </summary>
    private static readonly NumberFormatInfo TurkishNumbers = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ","
    };

    [HttpGet("{paymentId}")]
    public IActionResult Show(string paymentId)
    {
        var payment = payments.Find(paymentId);

        return payment is null ? NotFound() : Page(payment, message: null);
    }

    /// <param name="action"><c>pay</c> ya da <c>cancel</c>.</param>
    [HttpPost("{paymentId}")]
    public async Task<IActionResult> Decide(string paymentId, [FromForm] string? action, CancellationToken ct)
    {
        var payment = payments.Find(paymentId);

        if (payment is null)
        {
            return NotFound();
        }

        var decision = action switch
        {
            "pay" => PaymentStatus.Succeeded,
            "cancel" => PaymentStatus.Canceled,
            _ => (PaymentStatus?)null
        };

        if (decision is null)
        {
            return BadRequest();
        }

        if (!payments.Decide(payment, decision.Value))
        {
            return Page(payment, "Bu ödeme artık değiştirilemiyor.");
        }

        await webhooks.Writer.WriteAsync(
            new PaymentWebhook(payment, decision is PaymentStatus.Succeeded ? PaymentText.Succeeded : PaymentText.Canceled), ct);

        // 303: tarayıcı dönüş adresini GET ile açsın, formu yeniden göndermesin.
        Response.Headers.Location = payment.ReturnUrl;

        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private ContentResult Page(CardPayment payment, string? message)
    {
        var html = HtmlEncoder.Default;
        var status = payment.StatusAt(time.GetUtcNow());
        var amount = $"{payment.Amount.ToString("N2", TurkishNumbers)} {html.Encode(payment.Currency)}";

        var body = status is PaymentStatus.RequiresPayment
            ? $"""
               <form method="post">
                 <button name="action" value="pay">Öde</button>
                 <button name="action" value="cancel" class="secondary">Vazgeç</button>
               </form>
               """
            : $"""
               <p>Durum: <strong>{StatusText(status)}</strong></p>
               <p><a href="{html.Encode(payment.ReturnUrl)}">Geri dön</a></p>
               """;

        var notice = message is null ? string.Empty : $"<p class=\"notice\">{html.Encode(message)}</p>";

        return Content(
            $$"""
              <!doctype html>
              <html lang="tr">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Kart ödemesi (sahte sağlayıcı)</title>
                <style>
                  body { font-family: system-ui, sans-serif; max-width: 28rem; margin: 3rem auto; padding: 0 1rem; color: #1a1a1a; }
                  .amount { font-size: 2rem; font-weight: 600; margin: 1rem 0; }
                  .muted { color: #666; font-size: .9rem; }
                  .notice { background: #fff3cd; padding: .75rem; border-radius: .5rem; }
                  button { font-size: 1rem; padding: .6rem 1.4rem; border-radius: .5rem; border: 0; background: #635bff; color: #fff; cursor: pointer; margin-right: .5rem; }
                  button.secondary { background: #e5e5e5; color: #1a1a1a; }
                </style>
              </head>
              <body>
                <p class="muted">Sahte kart sağlayıcısı. Gerçek sağlayıcıda kart bilgisi ve 3D Secure bu sayfada.</p>
                <h1>Kart ödemesi</h1>
                <p class="amount">{{amount}}</p>
                {{notice}}
                {{body}}
              </body>
              </html>
              """,
            "text/html; charset=utf-8");
    }

    private static string StatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Succeeded => "Ödendi",
        PaymentStatus.Canceled => "Vazgeçildi",
        PaymentStatus.Expired => "Süresi doldu",
        _ => "Bekliyor"
    };
}

using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Sms.Fake;

/// <summary>
/// İstek tipleri burada, onboarding'deki istemcide ayrı yazılıyor: gerçek
/// entegrasyonda sağlayıcının dokümanından geliyorlar.
/// </summary>
public sealed record SendSmsRequest(string To, string Text);

public sealed record SmsAcceptedResponse(Guid MessageId);

public sealed record SmsInboxResponse(IReadOnlyList<SmsMessage> Items);

[ApiController]
[Route("v1/messages")]
public sealed class MessagesController(SmsInbox inbox, TimeProvider time) : ControllerBase
{
    /// <summary>Mesajı gönderilmiş sayar ve kutuya koyar.</summary>
    [HttpPost]
    [ProducesResponseType<SmsAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Send([FromBody] SendSmsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Text))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Alıcı ve metin zorunlu.");
        }

        var message = new SmsMessage(Guid.NewGuid(), request.To, request.Text, time.GetUtcNow());
        inbox.Add(message);

        return Accepted(new SmsAcceptedResponse(message.MessageId));
    }

    /// <summary>Kutudaki mesajlar, yeniden eskiye. Doğrulama kodunu deneyen kişi buradan okuyor.</summary>
    /// <param name="to">Alıcı, <c>+905XXXXXXXXX</c> biçiminde. Verilmezse hepsi.</param>
    /// <param name="size">En fazla 100.</param>
    [HttpGet]
    public SmsInboxResponse List([FromQuery] string? to, [FromQuery] int size = 20) =>
        new(inbox.For(to, Math.Clamp(size, 1, 100)));
}

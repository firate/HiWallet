using System.Threading.Channels;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Fakes.Topups;

/// <summary>
/// Para girişi tetikleyicisi. <b>Gerçek bir sağlayıcıda bu uç YOKTUR</b> — sağlayıcı
/// webhook'u müşteri para yatırdığında gönderir, sen istediğinde değil.
///
/// Ortak taban sınıf: iki sahte servis de aynı ucu açıyor ve tek fark temsil
/// ettikleri kurum. Her birine kopyalansaydı teslim modları zamanla ayrışır ve
/// iki testin sonucu karşılaştırılamaz olurdu.
/// </summary>
[ApiController]
[Route("v1/topups")]
public abstract class TopupsControllerBase(
    Channel<TopupDelivery> queue,
    IValidator<TopupRequest> validator) : ControllerBase
{
    /// <summary>
    /// Webhook gönderimini kuyruğa alır ve <c>202</c> döner.
    ///
    /// <c>202</c>, <c>200</c> değil: dönüldüğünde webhook henüz GÖNDERİLMEDİ,
    /// para da cüzdanda değil. Gecikmeli modda aradaki fark saniyeler.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TopupTriggerResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Trigger([FromBody] TopupRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var delivery = new TopupDelivery(
            request.WalletId,
            request.Amount,
            request.Currency.ToUpperInvariant(),
            request.Mode,
            request.Count,
            request.DelayMilliseconds);

        await queue.Writer.WriteAsync(delivery, ct);

        return Accepted(new TopupTriggerResponse(
            request.Mode.ToString(),
            EventCountFor(request)));
    }

    /// <summary>
    /// Kaç webhook gideceği. Testler bunu bekleyip inbox'ta o kadar satır arıyor —
    /// sayı burada hesaplanmasaydı her test modu kendi elleriyle bilmek zorunda
    /// kalırdı ve mod değiştiğinde sessizce yanlış sayıya bakarlardı.
    /// </summary>
    private static int EventCountFor(TopupRequest request) => request.Mode switch
    {
        TopupDeliveryMode.Duplicate => 2,
        TopupDeliveryMode.OutOfOrder => Math.Max(2, request.Count),
        _ => 1
    };
}

/// <param name="Mode">
/// <c>Normal</c> | <c>Duplicate</c> | <c>Delayed</c> | <c>OutOfOrder</c>.
/// </param>
public sealed record TopupRequest(
    Guid WalletId,
    decimal Amount,
    string Currency,
    TopupDeliveryMode Mode = TopupDeliveryMode.Normal,
    int Count = 3,
    int DelayMilliseconds = 500);

/// <param name="EventCount">
/// Kaç webhook gönderilecek. <c>Duplicate</c>'te 2 — ama ikisinin <c>eventId</c>'si
/// AYNI, dolayısıyla inbox'ta yalnızca bir satır oluşmalı.
/// </param>
public sealed record TopupTriggerResponse(string Mode, int EventCount);

public sealed class TopupRequestValidator : AbstractValidator<TopupRequest>
{
    /// <summary>
    /// Gecikme üst sınırı: sınırsız bırakılsaydı yanlış bir değer testi
    /// dakikalarca asardı. Bankanın senaryo ucundaki sınırla aynı gerekçe.
    /// </summary>
    private const int MaxDelayMilliseconds = 30_000;

    public TopupRequestValidator()
    {
        RuleFor(r => r.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunlu.");

        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency).NotEmpty().Length(3)
            .WithMessage("Para birimi üç harfli ISO kodu olmalı.");

        RuleFor(r => r.Mode).IsInEnum().WithMessage("Bilinmeyen teslim modu.");

        RuleFor(r => r.Count)
            .InclusiveBetween(2, 20)
            .When(r => r.Mode is TopupDeliveryMode.OutOfOrder)
            .WithMessage("Sırasız gönderimde event sayısı 2-20 aralığında olmalı.");

        RuleFor(r => r.DelayMilliseconds)
            .InclusiveBetween(0, MaxDelayMilliseconds)
            .WithMessage($"Gecikme en fazla {MaxDelayMilliseconds} ms olabilir.");
    }
}

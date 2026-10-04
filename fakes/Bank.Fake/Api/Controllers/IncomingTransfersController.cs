using FluentValidation;
using HiWallet.Bank.Fake.Api.Requests;
using HiWallet.Bank.Fake.Api.Responses;
using HiWallet.Bank.Fake.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Bank.Fake.Api.Controllers;

/// <summary>
/// Hesabımıza gelen havaleler: toplama hesabı. Bütün müşteriler aynı IBAN'a gönderiyor;
/// banka parayı açıklamaya bakmadan kabul ediyor ve bize iki yoldan haber veriyor —
/// bildirimle (asıl yol) ve hesap hareketleriyle (kontrol).
///
/// Bu tek kurum iki yönde çalışıyor: giden transferi <c>TransfersController</c> kabul
/// ediyor, gelen havaleyi burası bildiriyor. İkisi de aynı bildirim endpoint'ine gidiyor
/// ve gövdedeki <c>type</c> ayırıyor.
/// </summary>
[ApiController]
[Route("v1/incoming-transfers")]
public sealed class IncomingTransfersController(
    BankFakeStore store,
    IValidator<IncomingTransferRequest> validator,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// Havale gelmesini tetikler. <b>Gerçek bankada bu endpoint YOK.</b> <c>202</c>: para
    /// hesabımızda ama bildirim henüz gönderilmedi.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<IncomingTransferAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Receive([FromBody] IncomingTransferRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var transfer = new IncomingTransfer
        {
            BankReference = $"GLN{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Description = request.Description,
            SenderName = request.SenderName,
            SenderIban = request.SenderIban,
            SenderNationalId = request.SenderNationalId,
            ReceivedAt = timeProvider.GetUtcNow(),
            Notify = request.Notify
        };

        lock (store.Gate)
        {
            store.IncomingTransfers.Add(transfer);
        }

        return Accepted(new IncomingTransferAcceptedResponse(transfer.BankReference));
    }

    /// <summary>
    /// Hesap hareketleri: aralıkta gelen havaleler. Gerçek bankalarda sayfalı; sahte
    /// bankanın hafızası küçük, tek seferde dönüyor.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IncomingTransfersResponse>(StatusCodes.Status200OK)]
    public IncomingTransfersResponse Statement([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
    {
        lock (store.Gate)
        {
            return new IncomingTransfersResponse(store.IncomingTransfers
                .Where(t => t.ReceivedAt >= from && t.ReceivedAt <= to)
                .Select(t => new IncomingTransferItem(
                    t.BankReference, t.Amount, t.Currency, t.Description,
                    t.SenderName, t.SenderIban, t.SenderNationalId, t.ReceivedAt))
                .ToList());
        }
    }
}

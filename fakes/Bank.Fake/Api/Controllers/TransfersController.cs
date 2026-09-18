using FluentValidation;
using HiWallet.Bank.Fake.Api.Requests;
using HiWallet.Bank.Fake.Api.Responses;
using HiWallet.Bank.Fake.Application;
using Microsoft.AspNetCore.Mvc;

namespace HiWallet.Bank.Fake.Api.Controllers;

/// <summary>
/// Bankanın transfer endpoint'i. Canlıda bu controller'ın yerinde bankanın kendi
/// API'si var.
///
/// Kimlik doğrulama YOK: sahte bankanın endpoint'ini korumak, korunacak bir şey olmadığı
/// için tören olurdu. Gerçeğinde burada mTLS ya da OAuth olurdu ve o kısım
/// <c>bank-adapter</c>'ın işi.
/// </summary>
[ApiController]
[Route("v1/transfers")]
public sealed class TransfersController(
    AcceptTransferHandler handler,
    IValidator<StartTransferRequest> validator,
    TransferQueries transfers) : ControllerBase
{
    /// <summary>
    /// Bankanın idempotency başlığı. Adı bizim <c>Idempotency-Key</c>'imizle aynı
    /// olması tesadüf değil — yaygın kalıp — ama bu başlığı banka tanımlıyor.
    /// </summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// Transferi kabul eder. <b>Sonuç DÖNMÜYOR</b> — kesin sonuç callback ya da
    /// durum sorgusuyla, sonra (decisions.md madde 35).
    ///
    /// <c>202</c>, <c>200</c> değil: "aldım" ile "yaptım" aynı şey değil ve para bu
    /// response döndüğünde henüz hareket etmedi.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<StartTransferResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Start(
        [FromBody] StartTransferRequest request, CancellationToken ct)
    {
        // Anahtarsız request reddediliyor. Kabul edilseydi ağ kesintisinden sonraki
        // tekrar İKİNCİ BİR TRANSFER açardı — sahte de olsa bu davranışı taklit
        // etmek şart: adaptörün anahtarı gerçekten gönderdiğini bu kontrol kanıtlıyor.
        if (!Request.Headers.TryGetValue(IdempotencyHeader, out var key)
            || string.IsNullOrWhiteSpace(key))
        {
            ModelState.AddModelError(IdempotencyHeader, $"{IdempotencyHeader} başlığı zorunlu.");
            return ValidationProblem(ModelState);
        }

        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
        }

        var result = handler.Handle(
            new AcceptTransferCommand(
                request.ClientReference,
                key.ToString(),
                request.Amount,
                request.Currency.ToUpperInvariant(),
                request.DestinationIban));

        if (result.Unavailable)
        {
            // GEÇİCİ hata: transfer hiç kabul edilmedi, ortada kayıt yok. Adaptörün
            // yeniden denemesi bekleniyor ve saga'ya hiçbir şey bildirilmiyor.
            // Kalıcı başarısızlıkla karıştırılırsa her ağ kesintisi müşterinin
            // parasını ileri geri taşır (overview.md madde 6).
            Response.Headers.RetryAfter = "1";

            return Problem(
                title: "Banka şu an cevap veremiyor.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted(new StartTransferResponse(
            result.BankReference!, result.Status!, result.Replayed));
    }

    /// <summary>
    /// Transferin o anki durumu. <b>Mutabakat taramasının okuduğu endpoint.</b>
    ///
    /// Callback'i kaçırılmış bir transferin sonucu yalnızca buradan öğrenilebiliyor;
    /// bu yüzden bankanın böyle bir endpoint'i olmadığı durumda taramanın yapacak bir şeyi
    /// kalmıyor (decisions.md madde 35).
    /// </summary>
    [HttpGet("{bankReference}")]
    [ProducesResponseType<TransferStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TransferStatusResponse> Get(string bankReference)
    {
        var transfer = transfers.Find(bankReference);

        return transfer is null ? NotFound() : transfer;
    }
}

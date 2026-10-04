using FluentValidation;

namespace HiWallet.Bank.Fake.Api.Requests;

/// <summary>
/// Hesabımıza havale gelmesini tetikler. <b>Gerçek bankada bu endpoint YOK</b>: havaleyi
/// müşteri kendi bankasından gönderir, biz istediğimizde değil.
/// </summary>
/// <param name="Description">Gönderenin yazdığı açıklama; hesap numarası buraya yazılıyor.</param>
/// <param name="SenderNationalId">Gönderenin kimlik numarası; gönderen bankanın mesajla taşıdığı.</param>
/// <param name="Notify">
/// <c>false</c>: bildirim gönderilmiyor, havale yalnızca hesap hareketlerinde görünüyor.
/// Kaçırılmış bildirimi ve onu bulan taramayı denemek için.
/// </param>
public sealed record IncomingTransferRequest(
    decimal Amount,
    string Currency,
    string? Description,
    string? SenderName,
    string? SenderIban,
    string? SenderNationalId,
    bool Notify = true);

public sealed class IncomingTransferRequestValidator : AbstractValidator<IncomingTransferRequest>
{
    public IncomingTransferRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Para birimi üç harfli ISO kodu olmalı.");

        // Gerçek havalede açıklama 140 karakterle sınırlı; uzunu banka kesiyor.
        RuleFor(r => r.Description).MaximumLength(140);
    }
}

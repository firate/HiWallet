using FluentValidation;

namespace HiWallet.Bank.Fake.Api.Requests;

/// <summary>
/// Bankanın transfer başlatma sözleşmesi. <b>BANKANIN</b> sözleşmesi — bizim
/// <c>StartBankTransfer</c> mesajımızın kopyası değil ve paylaşılan bir assembly'den
/// gelmiyor (decisions.md madde 35). Adaptör bunu bankanın dokümanından yazıyor;
/// iki tarafın alan adları tesadüfen benzese de ayrı sözleşmeler.
/// </summary>
/// <param name="ClientReference">
/// Müşterinin kendi referansı — ISO 20022'deki <c>EndToEndIdentification</c>
/// karşılığı. Bizde saga kimliği. Banka bunu geri yansıtıyor, korelasyon bununla.
/// </param>
public sealed record StartTransferRequest(
    string ClientReference,
    decimal Amount,
    string Currency,
    string DestinationIban);

public sealed class StartTransferRequestValidator : AbstractValidator<StartTransferRequest>
{
    public StartTransferRequestValidator()
    {
        RuleFor(r => r.ClientReference).NotEmpty().WithMessage("Müşteri referansı zorunlu.");

        RuleFor(r => r.Amount).GreaterThan(0m).WithMessage("Tutar pozitif olmalı.");

        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Para birimi üç harfli ISO kodu olmalı.");

        // Banka IBAN'ı KENDİ kurallarıyla doğruluyor; bizim mod-97 kontrolümüzden
        // bağımsız. Burada yalnızca biçim bakılıyor — sahte bankanın gerçek bir
        // IBAN doğrulayıcısı taklit etmesi, sınanan şeyi bizden bankaya kaydırırdı.
        RuleFor(r => r.DestinationIban)
            .NotEmpty()
            .MinimumLength(15)
            .WithMessage("IBAN geçersiz.");
    }
}

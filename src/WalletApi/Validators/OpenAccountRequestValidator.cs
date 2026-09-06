using FluentValidation;
using HiWallet.WalletApi.Requests;

namespace HiWallet.WalletApi.Validators;

public sealed class OpenAccountRequestValidator : AbstractValidator<OpenAccountRequest>
{
    public OpenAccountRequestValidator()
    {
        // Tanınmayan tip sessizce Person'a düşmesin: enum JSON'da isim olarak
        // taşınıyor ve eşleşmeyen değer varsayılana (0) çöker.
        RuleFor(r => r.Type)
            .IsInEnum().WithMessage("Hesap tipi Person veya Business olmalı.");
    }
}

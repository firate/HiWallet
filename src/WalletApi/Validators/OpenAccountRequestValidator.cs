using FluentValidation;
using HiWallet.WalletApi.Requests;
using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletApi.Validators;

public sealed class OpenAccountRequestValidator : AbstractValidator<OpenAccountRequest>
{
    public OpenAccountRequestValidator()
    {
        // Tanınmayan tip sessizce Person'a düşmesin: enum JSON'da isim olarak
        // taşınıyor ve eşleşmeyen değer varsayılana (0) çöker.
        RuleFor(r => r.Type)
            .IsInEnum().WithMessage("Hesap tipi Person veya Business olmalı.");

        // Bireysel hesabı kayıt açıyor (POST /v1/person-accounts, yalnızca onboarding).
        // Buradan açılabilseydi kayıt ve doğrulama adımları atlanırdı.
        RuleFor(r => r.Type)
            .Equal(AccountType.Business).WithMessage("Bireysel hesap kayıt akışıyla açılıyor.");
    }
}

using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletApi.Validators;

/// <summary>
/// Para birimi ve tutar basamağı kuralları. Para hareket ettiren her request aynı
/// iki soruyu soruyor; iki kopya olsaydı biri değiştiğinde diğeri eski kuralla kalırdı.
/// </summary>
internal static class CurrencyRules
{
    public static bool IsValid(string? code)
    {
        if (code is null)
        {
            return false;
        }

        try
        {
            Currency.From(code);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool FitsMinorUnit(string code, decimal amount)
    {
        var currency = Currency.From(code);
        return decimal.Round(amount, currency.MinorUnit) == amount;
    }
}

using HiWallet.WalletService.Domain.Promos;

namespace HiWallet.WalletApi.Validators;

/// <summary>Promo'nun metin değerleri; tanınmayan değer <c>400</c>.</summary>
internal static class PromoTextRules
{
    public static bool IsScope(string? text) => Parses(text, PromoTexts.ScopeFromText);

    public static bool IsRule(string? text) => Parses(text, PromoTexts.RuleFromText);

    public static bool IsRewardType(string? text) => Parses(text, PromoTexts.RewardTypeFromText);

    private static bool Parses<T>(string? text, Func<string, T> parse)
    {
        if (text is null)
        {
            return false;
        }

        try
        {
            parse(text);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

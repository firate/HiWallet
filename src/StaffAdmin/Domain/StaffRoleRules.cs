using System.Text.RegularExpressions;

namespace HiWallet.StaffAdmin.Domain;

/// <summary>Panelde tanımlanan rolün adı; panelin üstünde ve kayıtta görünüyor.</summary>
public static partial class StaffRoleRules
{
    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 200;

    /// <summary>Harf, rakam, boşluk, tire ve alt çizgi; Türkçe harfler dahil.</summary>
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} _-]*$")]
    private static partial Regex NamePattern();

    public static bool IsWellFormed(string name) =>
        name.Length is >= 2 and <= MaxNameLength && NamePattern().IsMatch(name) && name == name.Trim();
}

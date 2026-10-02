using System.Text.RegularExpressions;
using HiWallet.Shared.Infrastructure.Authentication;

namespace HiWallet.StaffAdmin.Domain;

/// <summary>
/// Panelde tanımlanan rolün adı. Ad token'a yazılıyor ve panelin üstünde görünüyor;
/// bir iznin ya da kimlik sağlayıcının kendi rolünün adını alamıyor, yoksa token'da
/// rol ile izin ayırt edilemezdi.
/// </summary>
public static partial class StaffRoleRules
{
    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 200;

    /// <summary>Harf, rakam, boşluk, tire ve alt çizgi; Türkçe harfler dahil.</summary>
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} _-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^(default-roles-.+|offline_access|uma_authorization)$", RegexOptions.IgnoreCase)]
    private static partial Regex IdentityProviderRoles();

    public static bool IsWellFormed(string name) =>
        name.Length is >= 2 and <= MaxNameLength && NamePattern().IsMatch(name) && name == name.Trim();

    /// <summary>İzinlerin ve kimlik sağlayıcının kendi rollerinin adları, büyük-küçük harf farkı gözetmeden.</summary>
    public static bool IsReserved(string name) =>
        StaffPermissions.All.Contains(name, StringComparer.OrdinalIgnoreCase) || IdentityProviderRoles().IsMatch(name);
}

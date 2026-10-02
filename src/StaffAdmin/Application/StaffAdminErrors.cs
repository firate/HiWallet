namespace HiWallet.StaffAdmin.Application;

/// <summary>İş kuralı reddi → <c>422</c>. <see cref="Rule"/> gövdede panelin okuyacağı ad.</summary>
public sealed class StaffAdminRuleException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}

/// <summary>Rol ya da çalışan yok → <c>404</c>.</summary>
public sealed class StaffAdminNotFoundException(string message) : Exception(message);

/// <summary>Aynı adda rol ya da aynı e-postada çalışan var → <c>409</c>.</summary>
public sealed class StaffAdminConflictException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}

/// <summary>Panelin okuyacağı kural adları.</summary>
public static class StaffAdminRules
{
    /// <summary>Çalışan kendi rollerini değiştiremiyor ve kendini kapatamıyor.</summary>
    public const string OwnAccount = "own_account";

    /// <summary>Çalışan sahip olduğu rolü değiştiremiyor ve silemiyor: kendine yetki vermiş olurdu.</summary>
    public const string OwnRole = "own_role";

    public const string RoleNameReserved = "role_name_reserved";

    public const string RoleExists = "role_exists";

    public const string StaffExists = "staff_exists";

    /// <summary>Atanan rol panelin rolü değil (izin ya da kimlik sağlayıcının rolü) veya yok.</summary>
    public const string UnknownRole = "unknown_role";
}

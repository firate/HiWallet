using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence;

/// <summary>Aynı adda rol ya da aynı e-postada çalışan: tekilliği veritabanının index'i koruyor.</summary>
internal static class UniqueViolation
{
    public static bool Is(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

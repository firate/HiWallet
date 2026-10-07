using Npgsql;

namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Rastgele üretilen hesap numarası başka bir hesabınkiyle çakıştı. Tekilliği
/// <c>ux_accounts_number</c> koruyor; açılış yeni numarayla baştan deniyor. Çakışma
/// olasılığı hesap sayısıyla artıyor ama birkaç denemede sıfıra yakın.
/// </summary>
internal static class AccountNumberCollision
{
    public const int MaxAttempts = 5;

    public static bool Is(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_accounts_number"
        };
}

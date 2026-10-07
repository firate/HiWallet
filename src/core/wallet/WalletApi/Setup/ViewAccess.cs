using System.Security.Claims;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.WalletService.Application.Accounts;

namespace HiWallet.WalletApi.Setup;

/// <summary>
/// Görüntüleme uçlarının sahiplik kuralı. Müşteri yalnızca kullanıcısı olduğu hesabı
/// görüyor; çalışan her hesabı. Çalışanın bir rolü olduğunu uçtaki politika
/// (<see cref="HiWalletPolicies.CustomerOrStaff"/>) zaten kontrol etti.
///
/// Yalnızca okuma uçlarında: para hareketi başlatan uç çalışana kapalı ve orada üyelik
/// her zaman aranıyor.
/// </summary>
internal static class ViewAccess
{
    public static Task EnsureViewableAccountAsync(
        this AccountAccess access, ClaimsPrincipal user, Guid accountId, CancellationToken ct) =>
        user.IsEmployee() ? Task.CompletedTask : access.EnsureAccountAsync(user.Subject(), accountId, ct);

    public static Task EnsureViewableWalletAsync(
        this AccountAccess access, ClaimsPrincipal user, Guid walletId, CancellationToken ct) =>
        user.IsEmployee() ? Task.CompletedTask : access.EnsureWalletAsync(user.Subject(), walletId, ct);
}

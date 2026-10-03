using HiWallet.WalletService.Domain.Accounts;

namespace HiWallet.WalletService.Application.Accounts;

/// <summary>
/// Kimliğin kullanıcısı olduğu hesaplar, yeniden eskiye. Mobil uygulama yeni bir
/// cihazda hesabını buradan buluyor.
///
/// Sayfalama cursor ile (<c>baseline.md</c> madde 8). Sıra <c>(created_at, id)</c>;
/// cursor son hesabın kimliği.
/// </summary>
/// <param name="After">Önceki sayfanın son hesabının kimliği. İlk sayfada <c>null</c>.</param>
/// <param name="Size">İstenen sayfa boyutu. <see cref="AccountPage.MaxSize"/>'a çekiliyor.</param>
public sealed record ListAccountsQuery(string Subject, Guid? After, int Size);

/// <param name="KycLevel">Bireysel hesabın doğrulama seviyesi; işyeri hesabında <c>null</c>.</param>
public sealed record AccountSummary(
    Guid AccountId, AccountNumber Number, AccountType Type, KycLevel? KycLevel, DateTimeOffset CreatedAt);

/// <param name="NextCursor">Bir sonraki sayfanın <c>after</c> değeri. Son sayfada <c>null</c>.</param>
public sealed record AccountPage(IReadOnlyList<AccountSummary> Items, int Size, Guid? NextCursor)
{
    public const int MaxSize = 100;

    public const int DefaultSize = 20;
}

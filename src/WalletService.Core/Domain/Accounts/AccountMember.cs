namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Bir kimliğin bir hesap üzerinde işlem yapabildiği bilgisi. Kimlik, kimlik
/// sağlayıcıdaki kullanıcının <c>sub</c>'ı.
///
/// Sahiplik wallet'ta duruyor, kimlik sağlayıcıda değil: kimlik sağlayıcı kişinin kim
/// olduğunu söylüyor, hangi hesabın parasını yönettiğini ledger'ın sahibi biliyor.
/// Bir işyeri hesabının birden fazla kullanıcısı olabiliyor; bu yüzden hesaba kolon
/// değil, ayrı satır.
/// </summary>
public sealed class AccountMember
{
    private AccountMember()
    {
        // EF Core materialization.
    }

    private AccountMember(Guid accountId, string subject, DateTimeOffset createdAt)
    {
        AccountId = accountId;
        Subject = subject;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static AccountMember Of(Guid accountId, string subject, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Kimliksiz üyelik yazılamaz.", nameof(subject));
        }

        return new AccountMember(accountId, subject, createdAt);
    }
}

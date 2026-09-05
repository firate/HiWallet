namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Müşteri hesabı. Altında birden fazla cüzdan durur (decisions.md madde 20).
///
/// Müşteri yönetimi entity'si DEĞİL — ad, e-posta, KYC verisi burada durmaz, onlar bu
/// sistemin kapsamı dışında. Tek taşıdığı bilgi <see cref="Type"/>: bu hesap bir kişiye mi
/// yoksa işletmeye mi ait. Cüzdanlar buna kendileri karar veremez, yoksa aynı müşterinin
/// bir cüzdanı <c>person</c> diğeri <c>business</c> olabilirdi.
///
/// Limitlerin toplandığı kimlik de budur: günlük limit cüzdan bazında değil hesap
/// bazında uygulanır, aksi halde müşteri ikinci cüzdan açarak limiti aşar.
///
/// Şema: docs/ledger-schema.md "accounts".
/// </summary>
public sealed class Account
{
    private Account()
    {
        // EF Core materialization.
    }

    private Account(Guid id, AccountType type, DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public AccountType Type { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Account Open(Guid id, AccountType type, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Hesap kimliği boş olamaz.", nameof(id));
        }

        return new Account(id, type, createdAt);
    }
}

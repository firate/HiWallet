using HiWallet.WalletService.Domain.Errors;

namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Müşteri hesabı. Altında birden fazla cüzdan durur (decisions.md madde 20).
///
/// Müşteri yönetimi entity'si DEĞİL: ad, e-posta, TCKN ve doğrulamanın kanıtları
/// onboarding'in veritabanında; ledger'la aynı yerde durmuyor. Burada yalnızca para
/// hareketini belirleyenler var: <see cref="Type"/> (kişi mi işletme mi; cüzdanlar buna
/// kendileri karar veremez, yoksa aynı müşterinin bir cüzdanı <c>person</c> diğeri
/// <c>business</c> olabilirdi) ve bireysel hesapta doğrulamanın SONUCU
/// (<see cref="KycLevel"/>).
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

    private Account(Guid id, AccountNumber number, AccountType type, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Hesap kimliği boş olamaz.", nameof(id));
        }

        Id = id;
        Number = number;
        Type = type;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// İnsanın kullandığı numara: panelde arama, müşteriyle konuşma ve hesaba gelen para.
    /// Açılışta veriliyor ve değişmiyor; kimlik (<see cref="Id"/>) içeride kalıyor.
    /// </summary>
    public AccountNumber Number { get; private set; }

    public AccountType Type { get; private set; }

    /// <summary>
    /// Bireysel hesabın kime ait olduğu: kimlik sağlayıcıdaki <c>sub</c>. Bir kimliğin tek
    /// bireysel hesabı var ve açılış buna göre tekrar edilebilir. İşyeri hesabında
    /// <c>null</c>: birden fazla kullanıcısı olabiliyor ve kullanıcılar
    /// <c>account_members</c>'ta.
    /// </summary>
    public string? Holder { get; private set; }

    /// <summary>Bireysel hesabın doğrulama seviyesi. İşyeri hesabında <c>null</c>.</summary>
    public KycLevel? KycLevel { get; private set; }

    /// <summary>
    /// İşyeri platform fonlu promo ile ödeme kabul ediyor mu (decisions.md madde 37).
    /// Varsayılan <c>false</c>: anlaşmalı bir müşteri ile işyeri platform promo'sunu
    /// işyeri üzerinden nakde çevirebiliyor, kabul sözleşmesi olan işyeriyle sınırlı.
    /// İşyerinin kendi verdiği promo bu işarete bakmıyor. Backoffice gelene kadar SQL
    /// ile yönetiliyor.
    /// </summary>
    public bool AcceptsPromo { get; private set; }

    /// <summary>
    /// Bu ana kadar bankaya çekim kapalı (telefon değişikliğinden sonraki güvenlik süresi).
    /// Transfer ve ödeme açık; para hesaptan dışarı yalnızca çekimle çıkıyor.
    /// </summary>
    public DateTimeOffset? WithdrawalHoldUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Platform fonlu promo bu işyerinde geçsin mi (decisions.md madde 37). İşaret
    /// promo kabulünü sözleşmesi olan işyerleriyle sınırlıyor; bireysel hesapta anlamı yok.
    /// </summary>
    public void SetAcceptsPromo(bool acceptsPromo)
    {
        if (Type is not AccountType.Business)
        {
            throw new AccountRuleException("Promo kabulü yalnızca işyeri hesabında işaretlenir.");
        }

        AcceptsPromo = acceptsPromo;
    }

    /// <summary>
    /// Kaydı tamamlanan kimliğin hesabı. <see cref="Accounts.KycLevel.Unknown"/>'da açılıyor:
    /// kimlik henüz doğrulanmadı, para hareketi yok.
    /// </summary>
    public static Account OpenPerson(Guid id, AccountNumber number, string holder, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(holder))
        {
            throw new ArgumentException("Bireysel hesap bir kimliğe ait olmalı.", nameof(holder));
        }

        return new Account(id, number, AccountType.Person, createdAt)
        {
            Holder = holder,
            KycLevel = Accounts.KycLevel.Unknown
        };
    }

    public static Account OpenBusiness(Guid id, AccountNumber number, DateTimeOffset createdAt) =>
        new(id, number, AccountType.Business, createdAt);

    /// <summary>
    /// Seviyeyi yükseltir. Yükselten yollar birbirinden habersiz (kayıt, bankadan gelen
    /// para, backoffice); geç gelen bir alt seviye ulaşılmış üst seviyeyi geri almıyor.
    /// </summary>
    /// <returns>Seviye değiştiyse <c>true</c>; hesap zaten o seviyede ya da üstündeyse <c>false</c>.</returns>
    /// <exception cref="KycLevelNotApplicableException">Hesap bireysel değil.</exception>
    public bool RaiseKycLevel(KycLevel level)
    {
        if (Type is not AccountType.Person)
        {
            throw new KycLevelNotApplicableException(Id);
        }

        if (KycLevel >= level)
        {
            return false;
        }

        KycLevel = level;
        return true;
    }

    /// <summary>
    /// Çekim bu ana kadar kapalı; telefon numarası değişince onboarding koyuyor. Yalnızca
    /// uzuyor: art arda iki değişiklikte geç gelen kısa süre öncekini kısaltmıyor.
    /// </summary>
    /// <returns>Geçerli bekletmenin sonu.</returns>
    public DateTimeOffset HoldWithdrawalsUntil(DateTimeOffset until)
    {
        if (WithdrawalHoldUntil is not { } current || until > current)
        {
            WithdrawalHoldUntil = until;
        }

        return WithdrawalHoldUntil!.Value;
    }

    public bool WithdrawalsHeldAt(DateTimeOffset now) => WithdrawalHoldUntil > now;
}

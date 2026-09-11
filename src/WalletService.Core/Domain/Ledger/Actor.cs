namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// İşlemi başlatan taraf (<c>decisions.md</c> madde 34).
///
/// Tip ile kimlik BİRLİKTE taşınıyor: ikisi ayrı parametre olsaydı çağıran taraf
/// <c>Employee</c> tipiyle bir hesap kimliği geçebilirdi ve hiçbir şey itiraz etmezdi.
///
/// <b>Ledger'a iki kolon olarak yazılıyor</b>, tek bir nullable kolon olarak değil:
/// NULL "müşteri yaptı" ile "kaydetmeyi unuttuk"u aynı değere indirirdi.
/// </summary>
public readonly record struct Actor
{
    private Actor(ActorType type, string id)
    {
        Type = type;
        Id = id;
    }

    public ActorType Type { get; }

    /// <summary>
    /// <see cref="ActorType.Customer"/>'da hesabın kimliği,
    /// <see cref="ActorType.Employee"/>'de kimlik sağlayıcıdaki <c>sub</c>,
    /// <see cref="ActorType.System"/>'de akışın sabit adı.
    /// </summary>
    public string Id { get; }

    public static Actor Customer(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Müşteri aktörünün hesap kimliği boş olamaz.", nameof(accountId));
        }

        return new Actor(ActorType.Customer, accountId.ToString());
    }

    /// <param name="subject">
    /// Kimlik sağlayıcıdaki <c>sub</c>. E-posta ya da ad KULLANILMAZ — ikisi de
    /// değişiyor ve kalıcı bir kayıtta değişen bir kimlik işe yaramaz.
    /// </param>
    public static Actor Employee(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Çalışan aktörünün kimliği boş olamaz.", nameof(subject));
        }

        return new Actor(ActorType.Employee, subject);
    }

    /// <summary>
    /// Serbest metin almasının sebebi test ve ileride eklenecek akışlar; bilinen
    /// akışlar için <see cref="SystemActors"/> kullanılmalı, yazım hatası riski olmasın.
    /// </summary>
    public static Actor System(string flow)
    {
        if (string.IsNullOrWhiteSpace(flow))
        {
            throw new ArgumentException("Sistem aktörünün akış adı boş olamaz.", nameof(flow));
        }

        return new Actor(ActorType.System, flow);
    }
}

/// <summary>
/// İnsan müdahalesi olmadan ledger'a yazan akışlar. Kapalı bir küme; yeni bir akış
/// eklendiğinde buraya da eklenir.
/// </summary>
public static class SystemActors
{
    public static readonly Actor Topup = Actor.System("topup");

    public static readonly Actor Settlement = Actor.System("settlement");

    public static readonly Actor ProviderInvoice = Actor.System("provider-invoice");

    /// <summary>Çekim saga'sının komutları: düşme, iade, settlement.</summary>
    public static readonly Actor WithdrawalSaga = Actor.System("withdrawal-saga");
}

namespace HiWallet.Shared.Contracts.Actors;

/// <summary>
/// İşlemi başlatan taraf, MESAJ ÜZERİNDE (<c>decisions.md</c> madde 34).
///
/// <b>Neden düz string'ler.</b> Wallet tarafında <c>Actor</c> adında bir değer tipi
/// var ve fabrika metotlarıyla korunuyor; o tip buraya KONULAMAZ çünkü JSON
/// deserializer fabrikaları atlar ve kuyruktan gelen bayt geçersiz bir örnek
/// üretebilir. Sözleşme aptal, doğrulama sınırda: alıcı <c>Actor.From</c> ile
/// ayrıştırıyor ve tanımadığı tipte patlıyor.
///
/// <b>Neden mesajda taşınıyor.</b> Ledger'a yazan taraf ile başlatanı bilen taraf
/// farklı servisler. Taşınmazsa çalışanın başlattığı bir telafi ledger'a
/// <c>system</c> olarak düşer ve kimin karar verdiği kalıcı kayıtta kaybolur —
/// maddenin önlemek için yazıldığı şeyin ta kendisi.
/// </summary>
public sealed record CommandActor
{
    public required string Type { get; init; }

    public required string Id { get; init; }
}

/// <summary>
/// <see cref="CommandActor.Type"/>'ın alabileceği değerler. Hem mesaj sözleşmesinde
/// hem ledger'ın kolonunda aynı metinler kullanılıyor; tek kaynak burası.
/// </summary>
public static class ActorTypes
{
    public const string Customer = "customer";

    public const string Employee = "employee";

    public const string System = "system";
}

/// <summary>
/// İnsan müdahalesi olmadan ledger'a yazan akışların adları. Mesajda taşınan
/// <see cref="CommandActor.Id"/> bu değerlerden biri oluyor.
/// </summary>
public static class SystemFlows
{
    public const string Topup = "topup";

    public const string Settlement = "settlement";

    public const string ProviderInvoice = "provider-invoice";

    public const string WithdrawalSaga = "withdrawal-saga";
}

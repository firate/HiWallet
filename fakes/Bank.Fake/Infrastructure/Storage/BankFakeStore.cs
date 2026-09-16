namespace HiWallet.Bank.Fake.Infrastructure.Storage;

/// <summary>
/// Sahte bankanın bütün hafızası: transferler ve senaryolar. <b>Bellekte, kalıcı
/// DEĞİL</b> (decisions.md madde 35). Süreç yeniden başlayınca her şey siliniyor ve
/// bu bilinçli: sahte banka elle ve integration testlerle denemek için var,
/// geçmişi saklaması gereken bir sistem değil.
///
/// Bedeli: yeniden başlatmadan önce <c>pending</c> kalmış bir transferi mutabakat
/// taraması sorduğunda <c>404</c> alır ve o çekim kapanmaz. Gerçek banka transferini
/// unutmaz; bu davranış sahteye özgü ve yeniden başlatmayla birlikte bekleyen
/// çekimleri de gözden çıkarmak gerekiyor.
///
/// <b>Tek kilit.</b> Her okuma ve yazma <see cref="Gate"/> altında yapılır. İşlemler
/// bellek içi ve kısa; kabul adımı "anahtar var mı → senaryo → sayaç → kayıt"
/// dizisini bölünmeden yapmak zorunda, ayrı kilitler bunu karmaşıklaştırmaktan
/// başka bir şey kazandırmazdı. Veritabanındaki UNIQUE index'in yerini bu kilit tutuyor.
///
/// Tip public, üyeleri internal: controller'lar public olmak zorunda ve bağımlılık
/// zincirindeki tipler de öyle; içerik ise bu assembly'nin ve testlerin dışına açılmıyor.
/// </summary>
public sealed class BankFakeStore
{
    internal Lock Gate { get; } = new();

    /// <summary>Bankanın idempotency koruması. Yalnızca <see cref="Gate"/> altında kullanılır.</summary>
    internal Dictionary<string, BankTransfer> TransfersByKey { get; } = new(StringComparer.Ordinal);

    /// <summary>Durum sorgusu ve callback için. Yalnızca <see cref="Gate"/> altında kullanılır.</summary>
    internal Dictionary<string, BankTransfer> TransfersByReference { get; } = new(StringComparer.Ordinal);

    /// <summary>Client reference → senaryo. Yalnızca <see cref="Gate"/> altında kullanılır.</summary>
    internal Dictionary<string, TransferScenario> Scenarios { get; } = new(StringComparer.Ordinal);

    /// <summary>İki dizine birlikte ekler. Yalnızca <see cref="Gate"/> altında çağrılır.</summary>
    internal void Add(BankTransfer transfer)
    {
        TransfersByKey.Add(transfer.IdempotencyKey, transfer);
        TransfersByReference.Add(transfer.BankReference, transfer);
    }
}

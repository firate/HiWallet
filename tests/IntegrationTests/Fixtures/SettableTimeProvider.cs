namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>
/// İleri alınabilen "şimdi". Başka bir host'un saati testin ortasında değişmeli olduğunda:
/// sahte sağlayıcının oturumu ancak saati bitişi geçince süresi dolmuş sayıyor.
/// </summary>
public sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

namespace HiWallet.EdgeApi.InternalServices;

/// <summary>
/// Bir iç servisin adresi ve bekleme süresi. Her servis kendi bölümünden okunuyor:
/// <c>InternalServices:WalletApi</c>, <c>InternalServices:WithdrawalOrchestrator</c>.
/// </summary>
public sealed class InternalServiceOptions
{
    public const string SectionPrefix = "InternalServices";

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Tek denemenin sınırı. Aşıldığında istemci <c>503</c> alıyor; POST'ta iş iç
    /// serviste yine de tamamlanmış olabilir, istemci aynı <c>Idempotency-Key</c> ile
    /// tekrar gönderdiğinde işlem ikinci kez yapılmıyor.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

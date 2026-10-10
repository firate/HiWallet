namespace HiWallet.Shared.Contracts.DepositReturns;

// İadenin event'leri. Bankanın sonucu çekimdekiyle aynı event'lerle geliyor
// (BankTransferSucceeded, BankTransferFailed); orchestrator saga kimliğinden hangi saga
// olduğunu buluyor.

/// <summary>
/// wallet → orchestrator. Havale askıdan düşüldü, para clearing'de: bankaya gidebilir.
/// Bankaya giden komutun ihtiyacı olan her şeyi taşıyor; orchestrator havaleyi bilmiyor.
/// </summary>
public sealed record SuspenseDebitedForReturn
{
    public required Guid SagaId { get; init; }

    public required Guid LedgerTransactionId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Havaleyi alan banka; iade de ondan gidiyor.</summary>
    public required string Provider { get; init; }

    /// <summary>Havalenin bankadaki gelen işlem referansı.</summary>
    public required string DepositBankReference { get; init; }
}

/// <summary>
/// wallet → orchestrator. Düşme yapılmadı: havale yok, aktarılmış, iade edilmiş ya da
/// iadesi sürüyor. Hiçbir para hareketi olmadı.
/// </summary>
public sealed record SuspenseDebitForReturnRejected
{
    public required Guid SagaId { get; init; }

    /// <summary>Sebebin metni: log ve destek için.</summary>
    public required string Reason { get; init; }

    /// <summary>Makinenin okuyacağı kural; wallet-api'nin hata cevabındaki <c>rule</c> ile aynı.</summary>
    public required string Rule { get; init; }
}

/// <summary>wallet → orchestrator. İadenin muhasebesi kapandı: para nostro'dan çıktı.</summary>
public sealed record DepositReturnSettled
{
    public required Guid SagaId { get; init; }

    public required Guid LedgerTransactionId { get; init; }
}

/// <summary>wallet → orchestrator. Havale askıya geri kondu; yeniden karara açık.</summary>
public sealed record SuspendedDepositRestored
{
    public required Guid SagaId { get; init; }

    public required Guid LedgerTransactionId { get; init; }
}

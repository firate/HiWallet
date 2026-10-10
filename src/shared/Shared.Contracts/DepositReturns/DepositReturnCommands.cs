using HiWallet.Shared.Contracts.Actors;

namespace HiWallet.Shared.Contracts.DepositReturns;

// Askıdaki havalenin göndericiye iadesi. Saga orchestrator'da; komutlar çekim saga'sının
// kuyruklarından gidiyor ve aynı kurallarla işleniyor: CommandId ile deduplikasyon,
// SagaId ile korelasyon, alanlar init.

/// <summary>
/// orchestrator → wallet. "Bu havaleyi iade için askıdan düş."
///
/// Tutar TAŞIMIYOR: havalenin tutarını, para birimini ve bankasını wallet biliyor ve
/// cevabında bildiriyor. Orchestrator'a çalışandan yalnızca havalenin kimliği geliyor.
/// </summary>
public sealed record DebitSuspenseForReturn
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    /// <summary>Askı kaydının ledger işlemi.</summary>
    public required Guid SuspendedDepositId { get; init; }

    /// <summary>İadeyi isteyen çalışan; düşme kaydının aktörü.</summary>
    public required CommandActor Actor { get; init; }
}

/// <summary>
/// orchestrator → bank-adapter. "Bu havaleyi gönderene geri gönder."
///
/// IBAN TAŞIMIYOR, bilerek. Gönderenin IBAN'ı banka entegrasyonunun kaydında
/// (<c>bank_deposits</c>) duruyor ve oradan çıkmıyor: wallet'a giden havale mesajında da
/// yok. Adaptör havaleyi bankanın referansıyla bulup IBAN'ı kendisi okuyor.
/// </summary>
public sealed record ReturnBankDeposit
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    /// <summary>Havaleyi alan banka.</summary>
    public required string Provider { get; init; }

    /// <summary>Havalenin bankadaki gelen işlem referansı.</summary>
    public required string DepositBankReference { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}

/// <summary>
/// orchestrator → wallet. "Bu iadenin muhasebesini kapat."
///
/// <c>SettleWithdrawal</c>'ın aynası: tutarı wallet yazdı, ücreti yalnızca banka biliyor.
/// Ücreti platform yükleniyor; göndericiye tutarın tamamı dönüyor.
/// </summary>
public sealed record SettleDepositReturn
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }

    /// <summary>Bankanın kestiği ücret. Sıfır olabilir.</summary>
    public required decimal FeeAmount { get; init; }

    /// <summary>İade transferinin bankadaki referansı.</summary>
    public required string BankReference { get; init; }
}

/// <summary>
/// orchestrator → wallet. "İade gitmedi, havaleyi askıya geri koy."
///
/// Tutar taşımıyor: ters kayıt düşmenin aynası (<c>RefundWithdrawal</c> ile aynı gerekçe).
/// Aktör de taşımıyor: bu adımı çalışan değil bankanın reddi tetikliyor ve kaydın aktörü
/// iade saga'sı. Havale yeniden karara açılıyor.
/// </summary>
public sealed record RestoreSuspendedDeposit
{
    public required Guid CommandId { get; init; }

    public required Guid SagaId { get; init; }
}

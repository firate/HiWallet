using HiWallet.WithdrawalOrchestrator.Application.DepositReturns;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.WithdrawalOrchestrator.Api.Responses;

/// <param name="Replayed">Aynı anahtarla tekrar: yeni iade açılmadı.</param>
public sealed record DepositReturnAcceptedResponse(Guid DepositReturnId, string State, bool Replayed)
{
    public static DepositReturnAcceptedResponse From(StartDepositReturnResult result) =>
        new(result.DepositReturnId, result.State.ToText(), result.Replayed);
}

/// <summary>İadenin durumu. Tutar ve para birimi wallet askıdan düşene kadar boş.</summary>
/// <param name="State">
/// <c>initiated</c>, <c>rejected</c>, <c>bank_transfer_pending</c>, <c>settling</c>,
/// <c>completed</c>, <c>restoring</c>, <c>failed</c>.
/// </param>
/// <param name="FailureRule">
/// Wallet'ın reddinde kuralı (<c>deposit_already_resolved</c>, ...), banka reddinde
/// <c>bank_rejected</c>.
/// </param>
public sealed record DepositReturnResponse(
    Guid DepositReturnId,
    Guid SuspendedDepositId,
    string State,
    decimal? Amount,
    string? Currency,
    string RequestedBy,
    string? FailureReason,
    string? FailureRule,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DepositReturnResponse From(DepositReturnSaga saga) => new(
        saga.Id,
        saga.SuspendedDepositId,
        saga.State.ToText(),
        saga.Amount,
        saga.Currency,
        saga.RequestedBy,
        saga.FailureReason,
        saga.FailureRule,
        saga.CreatedAt,
        saga.UpdatedAt);
}

using HiWallet.WithdrawalOrchestrator.Application.DepositReturns;

namespace HiWallet.WithdrawalOrchestrator.Api.Requests;

/// <param name="SuspendedDepositId">İade edilecek havalenin askı kaydı.</param>
public sealed record StartDepositReturnRequest(Guid SuspendedDepositId)
{
    /// <summary>İsteyen token'dan, anahtar başlıktan; ikisi de gövdeden değil.</summary>
    public StartDepositReturnCommand ToCommand(string idempotencyKey, string requestedBy) =>
        new(SuspendedDepositId, requestedBy, idempotencyKey);
}

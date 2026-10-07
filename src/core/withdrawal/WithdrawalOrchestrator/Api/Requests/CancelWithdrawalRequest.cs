namespace HiWallet.WithdrawalOrchestrator.Api.Requests;

/// <param name="Reason">İptalin sebebi. Müşteriye çekimin durumunda gösteriliyor.</param>
public sealed record CancelWithdrawalRequest(string Reason);

using HiWallet.EdgeApi.Contracts;

namespace HiWallet.EdgeApi.InternalServices;

public static class Withdrawals
{
    /// <summary>
    /// Çekim başlatır. Çekimin idempotency kapsamı hesap; hesap cüzdanın sahibi olarak
    /// wallet-api'den okunuyor, istemcinin beyanından değil. Cüzdan çağıranın değilse
    /// wallet-api'nin 404'ü buradan dönüyor.
    /// </summary>
    public static async Task<WithdrawalAcceptedResponse> StartAsync(
        this WithdrawalOrchestratorClient orchestrator,
        WalletApiClient walletApi,
        CreateWithdrawalRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        var wallet = await walletApi.GetAsync<WalletResponse>($"v1/wallets/{request.WalletId}", ct);

        return await orchestrator.PostAsync<WithdrawalAcceptedResponse>(
            "v1/withdrawals",
            new StartWithdrawal(wallet.AccountId, request.WalletId, request.Amount, request.Currency, request.DestinationIban),
            idempotencyKey,
            ct);
    }

    /// <summary>withdrawal-orchestrator'ın beklediği gövde.</summary>
    private sealed record StartWithdrawal(
        Guid AccountId,
        Guid WalletId,
        decimal Amount,
        string Currency,
        string DestinationIban);
}

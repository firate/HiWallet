namespace HiWallet.WalletService.Infrastructure.Persistence;

/// <summary>
/// Kampanyalara göre değerlendirilmiş ödemelerin kaydı (decisions.md madde 37). İş
/// kavramı değil, değerlendirme işinin defteri — bu yüzden Domain'de değil burada.
///
/// Değerlendirme artan bir kimlik cursor'ıyla ilerlemiyor: <c>ledger_entries.id</c>
/// sırası commit sırası değil ve geç commit olan bir ödeme cursor'ın gerisinde
/// kalırdı. Ödeme, açtığı partilerle aynı transaction'da buraya işaretleniyor.
/// </summary>
internal sealed class PromoCampaignEvaluation
{
    public required Guid LedgerTransactionId { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }
}

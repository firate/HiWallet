using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>
/// Çekimin komisyon ve limit tarifesi. Saf hesaplama — DB'ye dokunmaz.
///
/// <b><see cref="TransferType"/>'a değer EKLENMEDİ, bilerek.</b> O enum iki cüzdan
/// arasındaki hareketi tanımlıyor: <c>Matches</c> gönderen ve alan hesap tipini
/// karşılaştırıyor, <c>ToLedgerType</c> transfer tiplerine eşliyor. Çekimde alan
/// cüzdan yok — para sistemden çıkıyor. Enum'a sığdırılsaydı iki metot da anlamsız
/// bir dal kazanırdı ve "transfer" adı yalan olurdu.
///
/// <b>Politika WALLET'ta, orchestrator'da değil.</b> Komisyon ve limit ledger'ın
/// bilgisi; iki serviste tekrarlanırsa ilk sapmada sessizce ayrışır ve müşteri
/// beklediğinden farklı öder (CLAUDE.md "Withdrawal saga").
/// </summary>
public sealed class WithdrawalPolicy(CommissionRate commission, TransferLimit limit)
{
    /// <summary>Hata mesajlarında ve alarmda görünen tarife adı.</summary>
    public const string Name = "Withdrawal";

    /// <summary>
    /// Müşterinin istediği tutar üzerinden kesilecek komisyon. Tutara EK olarak
    /// düşülüyor: müşteri 100 isterse cüzdandan 102 çıkıyor ve bankaya 100 gidiyor.
    /// </summary>
    public Money Commission(Money amount) => commission.Apply(amount);

    /// <param name="totalDebit">
    /// Cüzdandan çıkan TOPLAM, komisyon dahil (decisions.md madde 22). Yalnız istenen
    /// tutar kontrol edilseydi limit komisyon kadar aşılabilirdi.
    /// </param>
    public void EnsureWithinLimit(Guid accountId, Money totalDebit, Money spentToday) =>
        limit.Ensure(accountId, Name, totalDebit, spentToday);
}

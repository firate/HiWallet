namespace HiWallet.WalletService.Application.Deposits;

/// <summary>
/// Havale ledger'a hiç yazılamıyor: tutar ya da para birimi bozuk, ya da bu bankanın o
/// para biriminde sistem hesabı (nostro, askı) yok. Askıya bile alınamıyor.
///
/// Tüketici mesajı dead-letter'a yolluyor ve bu bir ALARM: para bankamızda ama ledger'da
/// yok, banka bakiyesiyle nostro ayrıştı. Yeniden denemek aynı sonucu verir; düzeltme
/// sistem hesabını açmak ya da sözleşmeyi düzeltmek.
/// </summary>
public sealed class DepositRejectedException(string provider, string bankReference, string reason)
    : Exception($"Havale ledger'a yazılamıyor ({provider}/{bankReference}): {reason}")
{
    public string Provider { get; } = provider;

    public string BankReference { get; } = bankReference;
}

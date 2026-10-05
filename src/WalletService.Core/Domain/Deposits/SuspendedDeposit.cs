using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Deposits;

/// <summary>
/// Cüzdana geçirilemeyen havalenin kaydı. Ledger DEĞİL: parası askı hesabında
/// (<see cref="LedgerAccountType.Suspense"/>), bu satır neden askıda olduğunu ve hangi
/// banka hareketi olduğunu söylüyor. Askı hesabının bakiyesi bu satırların toplamı.
///
/// Gönderenin adı, IBAN'ı ve kimlik numarası burada YOK: kişisel veri ledger'la aynı
/// yerde durmuyor, banka entegrasyonunun veritabanında kalıyor. Kaynağa iade orada
/// duran IBAN'a yapılacak.
/// </summary>
public sealed class SuspendedDeposit
{
    private SuspendedDeposit()
    {
    }

    /// <summary>Askı kaydının ledger işlemi; satırın anahtarı.</summary>
    public Guid LedgerTransactionId { get; private set; }

    /// <summary>Parayı alan banka; <c>ledger_accounts.provider</c> ile aynı değer.</summary>
    public string Provider { get; private set; } = null!;

    /// <summary>Bankanın gelen işlem referansı. Banka başına tekil.</summary>
    public string BankReference { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public DepositHoldReason Reason { get; private set; }

    /// <summary>
    /// Açıklamadaki numaranın gösterdiği hesap, bulunduysa. Gönderen o hesabın sahibi
    /// olmayabilir; çalışan iade ya da aktarım kararında bunu görüyor.
    /// </summary>
    public Guid? AccountId { get; private set; }

    /// <summary>Paranın bankaya girdiği an, bankanın bildirdiği.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Money Money => new(Amount, Currency);

    public static SuspendedDeposit Of(
        Guid ledgerTransactionId,
        string provider,
        string bankReference,
        Money amount,
        DepositHoldReason reason,
        Guid? accountId,
        DateTimeOffset receivedAt,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankReference);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Askıdaki tutar pozitif olmalı.", nameof(amount));
        }

        return new SuspendedDeposit
        {
            LedgerTransactionId = ledgerTransactionId,
            Provider = provider,
            BankReference = bankReference,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Reason = reason,
            AccountId = accountId,
            ReceivedAt = receivedAt,
            CreatedAt = createdAt
        };
    }
}

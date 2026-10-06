namespace HiWallet.WalletService.Domain.Promos;

/// <summary>
/// Bir partiden tüketilen tutar: harcama ya da süre sonu (decisions.md madde 37).
/// Append-only; hangisi olduğu <see cref="LedgerTransactionId"/>'deki işlemin tipinde.
///
/// Şema: docs/ledger-schema.md "promo_consumptions".
/// </summary>
public sealed class PromoConsumption
{
    private PromoConsumption()
    {
        // EF Core materialization.
    }

    public PromoConsumption(Guid grantId, Guid ledgerTransactionId, decimal amount, DateTimeOffset createdAt)
    {
        if (amount <= 0m)
        {
            throw new ArgumentException("Tüketim tutarı pozitif olmalı.", nameof(amount));
        }

        GrantId = grantId;
        LedgerTransactionId = ledgerTransactionId;
        Amount = amount;
        CreatedAt = createdAt;
    }

    /// <summary><c>bigserial</c>. DB üretir.</summary>
    public long Id { get; private set; }

    public Guid GrantId { get; private set; }

    public Guid LedgerTransactionId { get; private set; }

    /// <summary>Pozitif. Para birimi partininki.</summary>
    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

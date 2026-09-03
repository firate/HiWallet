namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
///     İşaretli tutar. İşaret yönü taşır: credit <c>+</c>, debit <c>-</c> (CLAUDE.md).
///     Hiçbir yerde tersine çevrilmez; sunum katmanı çevirmek isterse kendi yapar.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public Money(decimal amount, Currency currency)
    {
        if (decimal.Round(amount, currency.MinorUnit) != amount)
            throw new ArgumentException($"{currency} tutarı en fazla {currency.MinorUnit} ondalık basamak taşıyabilir: {amount}. Yuvarlama kararı çağıranın işi, sessizce yapılmaz.", nameof(amount));

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsCredit => Amount > 0m;

    public bool IsDebit => Amount < 0m;

    public Money Negated => new(-Amount, Currency);

    public Money Abs => new(Math.Abs(Amount), Currency);

    public int CompareTo(Money other)
    {
        AssertSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static Money Zero(Currency currency)
    {
        return new Money(0m, currency);
    }

    public static Money operator +(Money left, Money right)
    {
        AssertSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        AssertSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value)
    {
        return value.Negated;
    }

    public static bool operator <(Money left, Money right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(Money left, Money right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(Money left, Money right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(Money left, Money right)
    {
        return left.CompareTo(right) >= 0;
    }

    private static void AssertSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException($"Farklı para birimleri toplanamaz: {left.Currency} / {right.Currency}.");
        }
    }

    public override string ToString()
    {
        return $"{Amount.ToString($"F{Currency.MinorUnit}")} {Currency}";
    }
}
namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// ISO 4217 kodu. DB'de <c>char(3)</c>.
/// Tek para birimi kullanılsa bile tip baştan durur (decisions.md madde 6).
/// </summary>
public readonly record struct Currency
{
    private readonly string? _code;
    private readonly int _minorUnit;

    private Currency(string code, int minorUnit = 2)
    {
        _code = code;
        _minorUnit = minorUnit;
    }

    public string Code => _code ?? throw new InvalidOperationException("Currency initialize edilmemiş. default(Currency) kullanılamaz.");

    /// <summary>
    /// Tutarların oturmak zorunda olduğu ondalık basamak sayısı. TRY için 2 (kuruş),
    /// JPY için 0. Ledger'a yazılan her tutar müşterinin gerçekten tutabileceği ve
    /// çekebileceği bir şey olmalı — kuruşun altı yazılmaz.
    /// </summary>
    public int MinorUnit => _minorUnit;

    public static Currency From(string code, int minorUnit = 2)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Length != 3)
        {
            throw new ArgumentException($"ISO 4217 kodu 3 karakter olmalı: '{code}'.", nameof(code));
        }

        foreach (var c in code)
        {
            if (c is < 'A' or > 'Z')
            {
                throw new ArgumentException($"ISO 4217 kodu yalnızca büyük harf içerir: '{code}'.", nameof(code));
            }
        }

        return new Currency(code, minorUnit);
    }

    public override string ToString()
    {
        return Code;
    }
}

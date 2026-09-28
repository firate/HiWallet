namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Türkiye cep telefonu numarası, <c>+905XXXXXXXXX</c> biçiminde. Doğrulama kodu SMS
/// ile gidiyor; sabit hatta ve yurt dışı numarasına gitmiyor, onlar sınırda eleniyor.
/// </summary>
public readonly record struct PhoneNumber
{
    private const string CountryCode = "+90";

    private readonly string? _value;

    private PhoneNumber(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "PhoneNumber initialize edilmemiş. default(PhoneNumber) kullanılamaz.");

    /// <summary>Ekrana ve log'a giden hali: operatör kodu ve son iki hane.</summary>
    public string Masked => $"{CountryCode} {Value[3..6]} *** ** {Value[^2..]}";

    public override string ToString() => Masked;

    public static bool TryParse(string? value, out PhoneNumber phone)
    {
        phone = default;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var hasPlus = value.TrimStart().StartsWith('+');
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        // Harf ya da beklenmeyen işaret varsa numara değil.
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '+' or '-' or '(' or ')'))) return false;

        // Ülke kodunu ve baştaki sıfırı at: geriye on haneli ulusal numara kalmalı.
        var national = digits switch
        {
            { Length: 12 } when digits.StartsWith("90") => digits[2..],
            { Length: 11 } when !hasPlus && digits.StartsWith('0') => digits[1..],
            { Length: 10 } when !hasPlus => digits,
            _ => null
        };

        // Cep numarası 5 ile başlıyor.
        if (national is not { Length: 10 } || national[0] != '5') return false;

        phone = new PhoneNumber(CountryCode + national);
        return true;
    }

    public static PhoneNumber Parse(string? value) =>
        TryParse(value, out var phone)
            ? phone
            // Mesajda ham girdi YOK: kişisel veri ve log'a düşüyor.
            : throw new ArgumentException("Geçersiz cep telefonu numarası.", nameof(value));
}

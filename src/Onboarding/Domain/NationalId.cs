namespace HiWallet.Onboarding.Domain;

/// <summary>
/// Doğrulanmış biçimde T.C. kimlik numarası. Sınırda bir kez doğrulanıyor, sonra akışta
/// string olarak dolaşmıyor (IBAN ile aynı kalıp).
///
/// Doğrulama YAPISAL: on bir hane, ilk hane sıfır değil, son iki hane kontrol hanesi.
/// Numaranın gerçekten bu kişiye ait olduğunu nüfus kaydı söylüyor.
/// </summary>
public readonly record struct NationalId
{
    private const int Length = 11;

    private readonly string? _value;

    private NationalId(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "NationalId initialize edilmemiş. default(NationalId) kullanılamaz.");

    /// <summary>Ekrana ve log'a giden hali: ilk ve son iki hane.</summary>
    public string Masked => string.Concat(Value[..2], new string('*', Length - 4), Value[^2..]);

    public override string ToString() => Masked;

    /// <summary>Girdi dış dünyadan geliyor; hiçbir bozukluk istisnaya dönüşmüyor.</summary>
    public static bool TryParse(string? value, out NationalId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var digits = value.Replace(" ", string.Empty);

        if (digits.Length != Length || !digits.All(char.IsAsciiDigit) || digits[0] == '0') return false;

        var d = digits.Select(c => c - '0').ToArray();

        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];

        // Onuncu hane: (tek sıradakilerin toplamı × 7 − çift sıradakilerin toplamı) mod 10.
        // Fark negatif olabiliyor; C#'ın % işleci işareti koruyor, o yüzden 10 ekleniyor.
        if (((odd * 7 - even) % 10 + 10) % 10 != d[9]) return false;

        // On birinci hane: ilk on hanenin toplamı mod 10.
        if (d[..10].Sum() % 10 != d[10]) return false;

        id = new NationalId(digits);
        return true;
    }

    public static NationalId Parse(string? value) =>
        TryParse(value, out var id)
            ? id
            // Mesajda ham girdi YOK: geçersiz de olsa kişisel veri ve log'a düşüyor.
            : throw new ArgumentException("Geçersiz T.C. kimlik numarası.", nameof(value));
}

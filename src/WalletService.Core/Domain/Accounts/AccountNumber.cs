using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Hesabın insanın okuduğu, yazdığı ve telefonda söylediği numarası: 10 hane, ilk hane
/// sıfır değil, son hane Luhn kontrol hanesi. Hesabın kimliği (<see cref="Account.Id"/>)
/// içeride kalıyor; dışarıya numara çıkıyor. Bir kez veriliyor ve değişmiyor.
///
/// Rastgele, sıralı DEĞİL: sıralı numara kaç hesap olduğunu dışarı verir ve başkasının
/// numarasını tahmin ettirirdi. Kontrol hanesi tek hane hatasını ve yan yana iki hanenin
/// yer değiştirmesini (09 ile 90 hariç) sınırda yakalıyor; geçerli ama başka bir numara
/// yazılmasını yakalamıyor.
/// </summary>
public readonly partial record struct AccountNumber
{
    public const int Length = 10;

    private readonly string? _value;

    private AccountNumber(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "AccountNumber initialize edilmemiş. default(AccountNumber) kullanılamaz.");

    public override string ToString() => Value;

    /// <summary>Yeni numara. Tekilliği veritabanının index'i koruyor; çakışırsa açan yeniden üretiyor.</summary>
    public static AccountNumber New()
    {
        var body = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString(CultureInfo.InvariantCulture);

        return new AccountNumber(body + CheckDigit(body));
    }

    public static AccountNumber From(string? value)
    {
        if (!TryFrom(value, out var number))
        {
            // Mesajda ham girdi YOK: log'a düşüyor.
            throw new ArgumentException("Geçersiz hesap numarası.", nameof(value));
        }

        return number;
    }

    /// <summary>Girdi dış dünyadan geliyor; hiçbir biçim bozukluğu istisnaya dönüşmüyor.</summary>
    public static bool TryFrom(string? value, out AccountNumber number)
    {
        number = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Length != Length || normalized[0] == '0' || !normalized.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (CheckDigit(normalized[..^1]) != normalized[^1])
        {
            return false;
        }

        number = new AccountNumber(normalized);
        return true;
    }

    /// <summary>
    /// Serbest metindeki geçerli numaralar, tekrarsız ve metindeki sırasıyla: havale
    /// açıklaması. Numara bitişik ya da uygulamanın gösterdiği gibi 3-3-4 gruplanmış
    /// (boşluk ya da tireyle) aranıyor; önünde ya da arkasında rakam olan dizi daha uzun
    /// bir sayının parçası (TCKN, IBAN) sayılıyor ve alınmıyor. Kontrol hanesi tutmayan
    /// dizi numara değil.
    ///
    /// Birden fazla farklı numara çıkarsa hangisinin kastedildiği SEÇİLMİYOR: çağıran
    /// hepsini görüp karar veriyor.
    /// </summary>
    public static IReadOnlyList<AccountNumber> FindIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<AccountNumber>();

        foreach (Match match in Candidate().Matches(text))
        {
            var digits = match.Value.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            if (TryFrom(digits, out var number) && !found.Contains(number))
            {
                found.Add(number);
            }
        }

        return found;
    }

    /// <summary>
    /// <c>[0-9]</c>, <c>\d</c> değil: <c>\d</c> başka yazı sistemlerinin rakamlarını da
    /// eşliyor. Süre sınırı açıklama dış dünyadan geldiği için.
    /// </summary>
    [GeneratedRegex("(?<![0-9])[0-9]{3}[ -]?[0-9]{3}[ -]?[0-9]{4}(?![0-9])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Candidate();

    /// <summary>Luhn: sağdan başlayarak her ikinci hane iki katı, toplam onun katına tamamlanıyor.</summary>
    private static char CheckDigit(string body)
    {
        var sum = 0;

        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';

            if (i % 2 == 0)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return (char)('0' + (10 - sum % 10) % 10);
    }
}

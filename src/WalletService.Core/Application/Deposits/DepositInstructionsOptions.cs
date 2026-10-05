namespace HiWallet.WalletService.Application.Deposits;

/// <summary>
/// Müşterinin havale göndereceği toplama hesabı: bütün müşteriler aynı IBAN'a gönderiyor,
/// açıklamadaki hesap numarası paranın kime ait olduğunu söylüyor. Bankayla yapılan
/// sözleşmenin parçası, ortama göre değişiyor.
/// </summary>
public sealed class DepositInstructionsOptions
{
    public const string SectionName = "Deposits";

    public string Iban { get; set; } = string.Empty;

    /// <summary>Hesabın sahibi: alıcı adı olarak yazılacak şirket unvanı.</summary>
    public string AccountHolder { get; set; } = string.Empty;

    /// <summary>Havalenin para birimi. Toplama hesabı tek para biriminde.</summary>
    public string Currency { get; set; } = "TRY";
}

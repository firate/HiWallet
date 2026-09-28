namespace HiWallet.WalletService.Domain.Accounts;

/// <summary>
/// Bireysel hesabın doğrulama seviyesi. Hangi para hareketinin ne kadar yapılabildiğini
/// seviye belirliyor (<see cref="Policies.KycLimitPolicy"/>); doğrulamanın kendisi ve
/// kişisel veri onboarding'de, burada yalnızca sonucu duruyor.
///
/// Değerler arasında boşluk var: araya bir seviye girebilsin.
/// </summary>
public enum KycLevel
{
    /// <summary>Kayıt bitti, kimlik doğrulanmadı. Geçici; para hareketi yok.</summary>
    Unknown = 10,

    /// <summary>Telefon doğrulandı, kimlik bilgileri nüfus kaydıyla eşleşti.</summary>
    Unverified = 20,

    /// <summary>Müşterinin kendi adına banka hesabından para girdi; banka kimliği doğrulamıştı.</summary>
    Verified = 30,

    /// <summary>Uzaktan kimlik tespiti ya da fiziksel sözleşme.</summary>
    Contracted = 40
}

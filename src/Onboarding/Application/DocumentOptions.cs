namespace HiWallet.Onboarding.Application;

/// <summary>
/// Müşteriye gösterilen metinlerin güncel sürümleri. Onay bu sürümle yazılıyor; eski
/// sürümün onayı kabul edilmiyor. Metin değişince sürüm değişiyor.
/// </summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Onboarding:Documents";

    /// <summary>Kullanıcı sözleşmesi.</summary>
    public string Terms { get; set; } = string.Empty;

    /// <summary>KVKK aydınlatma metni.</summary>
    public string PrivacyNotice { get; set; } = string.Empty;
}

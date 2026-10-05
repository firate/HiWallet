namespace HiWallet.WalletService.Application.Abstractions;

/// <summary>
/// Hesap sahibinin kimlik numarasıyla karşılaştırma. Kimlik numarası wallet'ta
/// TUTULMUYOR: kişisel veri onboarding'in veritabanında. Soru onboarding'e soruluyor,
/// cevap yalnızca evet ya da hayır; numara wallet'a hiç yazılmıyor.
/// </summary>
public interface IHolderIdentity
{
    /// <param name="holder">Hesabın sahibi: kimlik sağlayıcıdaki <c>sub</c> (<c>accounts.holder</c>).</param>
    /// <param name="nationalId">Karşılaştırılacak kimlik numarası, bankanın bildirdiği gibi.</param>
    /// <returns>
    /// Sahibin doğrulanmış kimlik numarası bu mu. Sahip bilinmiyorsa ya da kimliği henüz
    /// doğrulanmamışsa <c>false</c>.
    /// </returns>
    /// <exception cref="Exception">Cevap alınamadı; geçici hata, yeniden denenmeli.</exception>
    Task<bool> IsHolderAsync(string holder, string nationalId, CancellationToken ct);
}

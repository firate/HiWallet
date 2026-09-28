using HiWallet.WalletService.Application.Accounts;

namespace HiWallet.WalletApi.Requests;

/// <param name="Holder">
/// Kaydı tamamlanan kimlik: kimlik sağlayıcıdaki <c>sub</c>. Onboarding kullanıcıyı
/// kimlik sağlayıcıda açtıktan sonra biliyor; müşterinin henüz token'ı yok.
/// </param>
public sealed record OpenPersonAccountRequest(string Holder)
{
    public OpenPersonAccountCommand ToCommand() => new(Holder);
}

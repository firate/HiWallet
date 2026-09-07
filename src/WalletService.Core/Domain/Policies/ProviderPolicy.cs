using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.WalletService.Domain.Policies;

/// <summary>
/// Sağlayıcının ücreti ne zaman aldığı (<c>decisions.md</c> madde 10). Global ayar
/// DEĞİL, sağlayıcı bazında: iki sağlayıcının aynı sistemde farklı modelle çalışması
/// gerçekçi.
/// </summary>
public enum FeeSettlement
{
    /// <summary>
    /// Ücret settlement anında kesiliyor, tutar o an belli (Stripe tarzı).
    /// <c>provider_expense</c> bacağı settlement kaydının parçası.
    /// </summary>
    Net = 1,

    /// <summary>
    /// Ücret işlem anında tahakkuk ediyor, ödeme dönem sonunda faturayla (bazı
    /// bankalar). Settlement kaydında <c>provider_expense</c> bacağı YOK; fatura
    /// geldiğinde tek toplu kayıt yazılıyor.
    /// </summary>
    Invoiced = 2
}

/// <summary>
/// Sağlayıcının işlem başına aldığı ücretin BEKLENTİSİ.
///
/// Gerçekleşen tutar değil: settlement ya da fatura geldiğinde ne alındığı belli
/// oluyor ve <c>provider_fees.actual_amount</c>'a o yazılıyor. Bu değer
/// <c>expected_amount</c>'a gidiyor ve <b>ledger'a asla yazılmıyor</b> — gerçekleşmiş
/// bir para hareketi değil (<c>decisions.md</c> madde 10).
/// </summary>
/// <param name="Rate">Oran (0.029 = %2.9). Tutara uygulanır.</param>
/// <param name="Fixed">İşlem başına sabit tutar. Kart sağlayıcılarında yaygın.</param>
public sealed record ProviderFeeTariff(decimal Rate, decimal Fixed = 0m)
{
    /// <summary>
    /// Yuvarlama komisyonla AYNI kuralda: en yakına, 0.5'te sıfırdan uzağa.
    /// Farklı olsaydı iki tarafın kuruşları sistematik olarak ayrışır ve fark
    /// mutabakatta "sağlayıcı oranı değiştirmiş" gibi görünürdü.
    /// </summary>
    public Money Expected(Money amount)
    {
        if (amount.Amount <= 0m)
        {
            throw new ArgumentException("Tutar pozitif olmalı.", nameof(amount));
        }

        var expected = decimal.Round(
            amount.Amount * Rate + Fixed, amount.Currency.MinorUnit, MidpointRounding.AwayFromZero);

        return new Money(expected, amount.Currency);
    }
}

/// <param name="Provider">
/// <c>ledger_accounts.provider</c> ile AYNI değer (<c>stripe-fake</c>). Sistem
/// hesapları da bu anahtarla ayrışıyor (madde 14); iki yerde farklı yazılsaydı
/// ücret kaydı ile clearing hesabı eşleşmezdi.
/// </param>
public sealed record ProviderTerms(
    string Provider,
    FeeSettlement FeeSettlement,
    ProviderFeeTariff Fee);

/// <summary>
/// Sağlayıcı kayıt defteri. Saf — DB'ye ve konfigürasyona dokunmaz.
///
/// <c>FeeOnFailure</c> (madde 18) BURADA YOK: yalnızca başarısız banka transferinde
/// anlamlı ve o yol henüz yazılmadı (5.5/5.6). Okunmayan konfigürasyon eklemek,
/// ayarın çalıştığı sanılan bir alan bırakırdı.
/// </summary>
public sealed class ProviderPolicy(IReadOnlyDictionary<string, ProviderTerms> terms)
{
    public IReadOnlyCollection<string> Providers => (IReadOnlyCollection<string>)terms.Keys;

    /// <summary>
    /// Tanınmayan sağlayıcı PATLIYOR, ücretsiz sayılmıyor. Sessizce sıfır ücret
    /// varsaymak, o sağlayıcının bütün giderini raporlardan siler ve fatura
    /// geldiğinde "beklenen toplam sıfır" ile uyuşmazlık üretirdi.
    /// </summary>
    public ProviderTerms For(string provider)
    {
        return terms.TryGetValue(provider, out var found)
            ? found
            : throw new UnknownProviderException(provider, Providers);
    }
}

public sealed class UnknownProviderException(string provider, IReadOnlyCollection<string> known)
    : InvalidOperationException(
        $"Tanımsız sağlayıcı: '{provider}'. Tanımlı olanlar: {string.Join(", ", known)}. " +
        "Ücret tarifesi olmayan bir sağlayıcının işlemi ücretsiz sayılmaz.")
{
    public string Provider { get; } = provider;
}

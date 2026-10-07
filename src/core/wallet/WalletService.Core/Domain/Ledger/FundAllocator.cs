using HiWallet.WalletService.Domain.Errors;

namespace HiWallet.WalletService.Domain.Ledger;

/// <summary>
/// Tek bir kovadan çıkan pay. <see cref="Amount"/> alıcıya giden,
/// <see cref="Commission"/> <c>revenue</c>'ya giden kısım; gönderen bacağı ikisinin
/// toplamı kadar düşüyor.
/// </summary>
public readonly record struct FundAllocation(FundType FundType, Money Amount, Money Commission)
{
    /// <summary>Bu kovadan cüzdandan çıkan toplam.</summary>
    public Money Debit => Amount + Commission;
}

/// <summary>
/// Transferin kovalara dağıtımı (decisions.md madde 36). Saf — DB, zaman ve
/// konfigürasyon bilmez.
///
/// <b>Sıra sabit ve konfigüre EDİLMEZ:</b> <see cref="FundTypes.SpendOrder"/>, yani
/// en kısıtlı kova önce eriyor. Ters sırada müşteri nakdini harcayıp çekilemeyen
/// bakiyeyle kalırdı. Kural mevzuattan geliyor, tarife ayarı değil.
///
/// <b>Önce tutar, sonra komisyon.</b> Her kovadan çıkanı tutar/komisyon oranında
/// bölmek de mümkündü ama kuruş yuvarlaması kova bazındaki dengeyi bozardı. Bu
/// sırada her kovanın bacakları kendi içinde sıfırlanıyor: gönderenden çıkan =
/// alıcıya giden + komisyon.
/// </summary>
public static class FundAllocator
{
    /// <summary>
    /// Cüzdandan cüzdana transfer için dağıtım. <see cref="FundType.Promo"/> hiç yer
    /// almıyor (<see cref="FundTypes.CanTransfer"/>): karşılığında fon yatırılmamış
    /// bir bakiye başkasına geçebilseydi fiilen nakde yakın bir araca dönerdi.
    ///
    /// Kova tipi karşı tarafta AYNEN korunuyor; korunmasaydı kart kısıtı tek adımda
    /// delinirdi (kartla yükle, ikinci hesabına gönder, oradan IBAN'a çek).
    /// </summary>
    /// <param name="available">
    /// Cüzdanın kova başına bakiyesi. Transfer edilemeyen kovalar da verilebilir,
    /// dağıtım onları atlıyor.
    /// </param>
    /// <exception cref="InsufficientFundsException">
    /// Transfer edilebilir kovaların toplamı <paramref name="amount"/> artı
    /// <paramref name="commission"/>'u karşılamıyorsa. Cüzdanın TOPLAM bakiyesi yetse
    /// bile atılıyor — müşterinin gördüğü toplam ile çıkabilen tutar ayrı şeyler.
    /// </exception>
    public static IReadOnlyList<FundAllocation> ForTransfer(
        Guid walletId,
        IReadOnlyDictionary<FundType, Money> available,
        Money amount,
        Money commission)
    {
        var currency = amount.Currency;

        if (commission.Currency != currency)
        {
            throw new InvalidOperationException(
                $"Tutar {currency}, komisyon {commission.Currency} — aynı para biriminde olmalı.");
        }

        var usable = FundTypes.SpendOrder
            .Where(FundTypes.CanTransfer)
            .Select(fundType => (FundType: fundType, Remaining: Balance(available, fundType, currency)))
            .Where(bucket => bucket.Remaining > 0m)
            .ToList();

        var transferable = usable.Sum(bucket => bucket.Remaining);
        var required = amount.Amount + commission.Amount;

        if (transferable < required)
        {
            throw new InsufficientFundsException(
                walletId, new Money(transferable, currency), new Money(required, currency));
        }

        // Önce tutar, sonra komisyon — ikisi de AYNI sırayı izliyor, yani komisyon
        // tutarın bıraktığı kalanlardan devam ediyor.
        var remaining = usable.ToDictionary(bucket => bucket.FundType, bucket => bucket.Remaining);

        var takenAmount = Distribute(usable, remaining, amount.Amount);
        var takenCommission = Distribute(usable, remaining, commission.Amount);

        var legs = new List<FundAllocation>(usable.Count);

        foreach (var (fundType, _) in usable)
        {
            var fromAmount = takenAmount.GetValueOrDefault(fundType);
            var fromCommission = takenCommission.GetValueOrDefault(fundType);

            // Hiç katkısı olmayan kova bacak açmıyor: sıfır tutarlı entry ledger'a
            // yazılmaz, hiçbir şey ifade etmiyor.
            if (fromAmount == 0m && fromCommission == 0m) continue;

            legs.Add(new FundAllocation(
                fundType, new Money(fromAmount, currency), new Money(fromCommission, currency)));
        }

        return legs;
    }

    /// <summary>
    /// Ödeme için dağıtım (decisions.md madde 37). Promo yalnızca tutarı karşılıyor,
    /// komisyonu karşılamıyor; tutarın kalanı ve komisyon <see cref="ForTransfer"/>
    /// ile <c>card</c> → <c>cash</c> sırasında dağıtılıyor.
    ///
    /// Promo bacağının alıcı tarafı <c>cash</c> — o dönüşüm çağıranın işi, burası
    /// yalnızca gönderenden hangi kovadan ne kadar çıktığını söylüyor.
    /// </summary>
    /// <param name="usablePromo">
    /// Alıcı işyerinde geçerli partilerin kalanlarının toplamı. Cüzdanın promo
    /// kovasının tamamı olmayabilir: kovadaki bazı partiler başka işyerleriyle
    /// kısıtlı ya da süresi dolmuş olabilir.
    /// </param>
    public static IReadOnlyList<FundAllocation> ForPayment(
        Guid walletId,
        IReadOnlyDictionary<FundType, Money> available,
        Money usablePromo,
        Money amount,
        Money commission)
    {
        var currency = amount.Currency;
        var fromPromo = new Money(Math.Min(usablePromo.Amount, amount.Amount), currency);

        var transferable = FundTypes.SpendOrder
            .Where(FundTypes.CanTransfer)
            .Sum(fundType => Balance(available, fundType, currency));

        if (fromPromo.Amount + transferable < amount.Amount + commission.Amount)
        {
            throw new InsufficientFundsException(
                walletId, new Money(fromPromo.Amount + transferable, currency), amount + commission);
        }

        var legs = new List<FundAllocation>();

        if (!fromPromo.IsZero)
        {
            legs.Add(new FundAllocation(FundType.Promo, fromPromo, Money.Zero(currency)));
        }

        legs.AddRange(ForTransfer(walletId, available, amount - fromPromo, commission));

        return legs;
    }

    /// <summary>
    /// <paramref name="required"/>'ı kovalara sırayla dağıtır ve
    /// <paramref name="remaining"/>'i tüketir. Toplam yeterliliği çağıran zaten
    /// doğruladı, bu yüzden burada eksik kalma ihtimali yok.
    /// </summary>
    private static Dictionary<FundType, decimal> Distribute(
        List<(FundType FundType, decimal Remaining)> order,
        Dictionary<FundType, decimal> remaining,
        decimal required)
    {
        var taken = new Dictionary<FundType, decimal>();

        foreach (var (fundType, _) in order)
        {
            if (required <= 0m) break;

            var take = Math.Min(remaining[fundType], required);

            taken[fundType] = take;
            remaining[fundType] -= take;
            required -= take;
        }

        return taken;
    }

    private static decimal Balance(
        IReadOnlyDictionary<FundType, Money> available, FundType fundType, Currency currency)
    {
        if (!available.TryGetValue(fundType, out var money)) return 0m;

        if (money.Currency != currency)
        {
            throw new InvalidOperationException(
                $"{fundType} kovası {money.Currency} tutuyor, {currency} istendi.");
        }

        return money.Amount;
    }
}

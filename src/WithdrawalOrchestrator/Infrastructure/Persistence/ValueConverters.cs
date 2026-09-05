using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Domain tipleri ↔ kolon değerleri.
///
/// Durum enum'u DB'de metin: operasyonun elle bakacağı bir alan ve <c>state = 4</c>
/// bir insana hiçbir şey söylemiyor. Ayrıca enum'a ortadan bir değer eklemek int
/// eşlemesini sessizce kaydırırdı. Metnin kendisi
/// <see cref="WithdrawalStates.ToText"/>'te — kolon, HTTP yanıtı ve index filtresi
/// aynı kaynaktan besleniyor.
/// </summary>
internal static class ValueConverters
{
    /// <summary>
    /// Doğrulanmış IBAN ↔ <c>text</c>. Okurken <see cref="Iban.From"/> yeniden
    /// doğruluyor: DB'ye elle yazılmış bozuk bir değer sessizce akışa girmesin.
    /// </summary>
    public static readonly ValueConverter<Iban, string> Iban =
        new(iban => iban.Value, value => Domain.Iban.From(value));

    public static readonly ValueConverter<WithdrawalState, string> WithdrawalState =
        new(state => state.ToText(), text => WithdrawalStates.FromText(text));
}

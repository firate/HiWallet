using HiWallet.WithdrawalOrchestrator.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Domain tipleri ↔ kolon değerleri. Enum'lar DB'de snake_case text; eşleme AÇIKÇA
/// yazılır, EF'in convention'ına bırakılmaz (structure.md "Adlandırma").
///
/// Enum'u <c>int</c> olarak saklamıyoruz: saga durumu operasyonun elle bakacağı bir
/// alan ve "state = 4" bir insana hiçbir şey söylemiyor. Ayrıca enum'a ortadan bir
/// değer eklemek int eşlemesini sessizce kaydırırdı.
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
        new(state => ToText(state), text => ToState(text));

    private static string ToText(WithdrawalState state)
    {
        return state switch
        {
            Domain.WithdrawalState.Initiated => "initiated",
            Domain.WithdrawalState.Rejected => "rejected",
            Domain.WithdrawalState.Debited => "debited",
            Domain.WithdrawalState.BankTransferPending => "bank_transfer_pending",
            Domain.WithdrawalState.Completed => "completed",
            Domain.WithdrawalState.Compensating => "compensating",
            Domain.WithdrawalState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Eşlemesi yazılmamış saga durumu.")
        };
    }

    private static WithdrawalState ToState(string text)
    {
        return text switch
        {
            "initiated" => Domain.WithdrawalState.Initiated,
            "rejected" => Domain.WithdrawalState.Rejected,
            "debited" => Domain.WithdrawalState.Debited,
            "bank_transfer_pending" => Domain.WithdrawalState.BankTransferPending,
            "completed" => Domain.WithdrawalState.Completed,
            "compensating" => Domain.WithdrawalState.Compensating,
            "failed" => Domain.WithdrawalState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Bilinmeyen saga durumu.")
        };
    }
}

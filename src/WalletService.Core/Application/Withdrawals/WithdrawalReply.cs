using System.Text.Json;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <summary>
/// Komuta verilen cevap: yayınlanacak event'in routing key'i ve gövdesi.
///
/// Handler event NESNESİ değil SERİLEŞTİRİLMİŞ hali dönüyor: aynı gövde iki yere
/// gidiyor, broker'a ve <c>processed_messages</c>'a. Tek bir serileştirme, tekrar
/// teslimde saklanandan farklı bir mesaj yayınlanmasını engelliyor.
///
/// <b>Bayt bazında aynı DEĞİL.</b> <c>reply_payload</c> kolonu <c>jsonb</c> ve
/// Postgres anahtar sırasını normalize ediyor; tekrar teslimde yayınlanan gövde
/// ANLAMCA aynı, karakter karakter değil. Tüketici JSON okuduğu için fark etmiyor —
/// ama bu gövde üzerinde bir imza ya da hash hesaplanacaksa bu varsayım kırılır.
/// (Top-up inbox'ı da aynı şekilde çalışıyor.)
/// </summary>
/// <param name="Replayed">
/// Komut daha önce işlenmiş; ledger'a DOKUNULMADI, saklanan cevap aynen dönüldü.
/// </param>
public sealed record WithdrawalReply(string RoutingKey, string Payload, bool Replayed)
{
    /// <summary>
    /// Orchestrator'ın tüketicisiyle AYNI olmak zorunda (camelCase). Ayrışırsa mesaj
    /// karşı tarafta boş alanlarla deserialize olur ve hata vermez.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static WithdrawalReply For<T>(T @event, bool replayed = false) =>
        new(typeof(T).Name, JsonSerializer.Serialize(@event, JsonOptions), replayed);
}

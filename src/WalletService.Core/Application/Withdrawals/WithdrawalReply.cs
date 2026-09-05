using System.Text.Json;

namespace HiWallet.WalletService.Application.Withdrawals;

/// <summary>
/// Komuta verilen cevap: yayınlanacak event'in routing key'i ve gövdesi.
///
/// Handler event NESNESİ değil SERİLEŞTİRİLMİŞ hali dönüyor, çünkü aynı bayt dizisi
/// iki yere gidiyor: broker'a ve <c>processed_messages</c>'a. İki ayrı serileştirme
/// olsaydı tekrar teslimde yayınlanan mesaj ilk gönderilenden farklı olabilirdi.
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

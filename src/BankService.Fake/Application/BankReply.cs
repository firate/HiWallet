using System.Text.Json;

namespace HiWallet.BankService.Application;

/// <summary>
/// Komuta verilen cevap: yayınlanacak event'in routing key'i ve gövdesi.
/// Wallet tarafındaki karşılığıyla aynı kalıp — handler serileştirilmiş hali
/// dönüyor, çünkü aynı gövde hem broker'a hem <c>bank_transfers</c>'a gidiyor.
/// </summary>
/// <param name="Replayed">
/// Komut daha önce işlenmiş; transfer TEKRAR YAPILMADI, saklanan cevap dönüldü.
/// </param>
public sealed record BankReply(string RoutingKey, string Payload, bool Replayed)
{
    /// <summary>
    /// Orchestrator'ın tüketicisiyle AYNI olmak zorunda (camelCase). Ayrışırsa mesaj
    /// karşı tarafta boş alanlarla deserialize olur ve hata vermez.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static BankReply For<T>(T @event) =>
        new(typeof(T).Name, JsonSerializer.Serialize(@event, JsonOptions), Replayed: false);
}

/// <summary>
/// Geçici banka hatası. Cevap ÜRETİLMİYOR: mesaj ack'lenmiyor, komut yeniden teslim
/// ediliyor ve bir sonraki denemede senaryo ilerliyor. Saga bu sırada
/// <c>BankTransferPending</c>'de bekliyor — her geçici hatada telafi başlatmak
/// müşterinin parasını gereksiz yere ileri geri taşırdı (overview.md madde 6).
/// </summary>
public sealed class TransientBankFailureException(Guid sagaId, int remaining)
    : Exception($"Banka geçici olarak cevap vermedi. Saga {sagaId}, kalan deneme {remaining}.");

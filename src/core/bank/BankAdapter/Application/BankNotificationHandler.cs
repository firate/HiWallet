using System.Text.Json;
using HiWallet.BankIntegration.Domain;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Bankanın bildirimini anlamlandırır. Banka tek endpoint'e iki tür bildirim gönderiyor:
/// başlattığımız transferin sonucu ve hesabımıza gelen havale. Gövdedeki <c>type</c>
/// hangisi olduğunu söylüyor; tipi olmayan bildirim transfer sonucu sayılıyor (tip
/// alanından önceki tek bildirim oydu).
///
/// Tanınmayan tip TAHMİN EDİLMİYOR: istisna atılıyor, satır inbox'ta işlenmemiş kalıyor
/// ve alarm konusu oluyor. Sözleşme değişmiş olabilir.
/// </summary>
internal sealed class BankNotificationHandler(
    TransferCompleter completer,
    DepositRecorder deposits,
    ILogger<BankNotificationHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="provider">Bildirimi gönderen banka; inbox satırının kurumu.</param>
    public async Task ApplyAsync(string provider, string rawPayload, CancellationToken ct)
    {
        switch (TypeOf(rawPayload))
        {
            case BankEventType.TransferStatus:
                await ApplyTransferStatusAsync(rawPayload, ct);
                return;

            case BankEventType.IncomingTransfer:
                var incoming = Read<IncomingTransferNotification>(rawPayload);
                await deposits.RecordAsync(provider, incoming.ToItem(), DepositRecorder.ViaCallback, ct);
                return;

            case var unknown:
                throw new InvalidOperationException(
                    $"Bankanın bilinmeyen bildirim tipi: '{unknown}'. Sözleşme değişmiş olabilir.");
        }
    }

    private async Task ApplyTransferStatusAsync(string rawPayload, CancellationToken ct)
    {
        var notification = Read<CallbackNotification>(rawPayload);
        var status = BankClient.Map(notification.Status);

        if (status is null)
        {
            // Bankanın tanımadığımız bir durum kelimesi. Tahmin ETMİYORUZ: yanlış
            // tahmin ya müşterinin parasını gereksiz geri gönderir ya da hiç
            // gitmemiş parayı gitmiş gösterir.
            throw new InvalidOperationException(
                $"Bankanın bilinmeyen durumu: '{notification.Status}'. Sözleşme değişmiş olabilir.");
        }

        if (status is BankTransferStatus.Pending)
        {
            // Banka "hâlâ bekliyor" diye callback göndermiş. İşlenmiş sayılıyor —
            // kapatacak bir şey yok ve satırı açık bırakmak alarmı kirletirdi.
            logger.LogInformation(
                "Bekliyor bildirimi, kapatılacak bir şey yok. {BankReference}", notification.BankReference);

            return;
        }

        await completer.ResolveAsync(
            notification.BankReference,
            status.Value,
            notification.Fee,
            notification.FailureReason,
            TransferCompleter.ViaCallback,
            ct);
    }

    private static string TypeOf(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);

        return document.RootElement.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String
            ? type.GetString()!
            : BankEventType.TransferStatus;
    }

    private static T Read<T>(string rawPayload) =>
        JsonSerializer.Deserialize<T>(rawPayload, JsonOptions)
        ?? throw new JsonException("Bildirim gövdesi boş.");
}

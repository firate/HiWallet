using System.Text.Json;
using HiWallet.BankIntegration.Persistence;
using HiWallet.Shared.Contracts.Deposits;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.BankAdapter.Application;

/// <summary>
/// Gelen havaleyi KAYDEDEN tek kod. İki yol buraya varıyor — bankanın bildirimi ve hesap
/// hareketi taraması — ve ikisi aynı işi yapmak zorunda (<see cref="TransferCompleter"/>
/// ile aynı gerekçe). Aynı havale iki yoldan da gelirse ikincisi yazılmıyor.
///
/// <b>Mesaj YAYINLANMIYOR, SAKLANIYOR.</b> Wallet'a gidecek gövde burada üretilip satırla
/// birlikte yazılıyor, yayını relay yapıyor: "havaleyi gördük" ile "wallet'a bildirdik"
/// aynı ana bağlanırsa broker erişilemezken havale ya kaybolur ya da görülmemiş sayılırdı.
///
/// Gövdede gönderenin adı ve IBAN'ı YOK: wallet'ın kararı için gerekmiyor, kişisel veri
/// burada kalıyor. Kimlik numarası gidiyor; gönderenin hesap sahibi olduğu onunla
/// doğrulanıyor.
/// </summary>
internal sealed class DepositRecorder(
    IDbContextFactory<BankDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<DepositRecorder> logger)
{
    /// <summary>Havaleyi bankanın bildirimi getirdi.</summary>
    public const string ViaCallback = "callback";

    /// <summary>Havaleyi hesap hareketi taraması getirdi. Bu değerin artması alarm konusu.</summary>
    public const string ViaReconciliation = "reconciliation";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <returns>Yeni yazıldıysa <c>true</c>; aynı havale zaten kayıtlıysa <c>false</c>.</returns>
    public async Task<bool> RecordAsync(
        string provider, IncomingTransferItem incoming, string via, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        var payload = JsonSerializer.Serialize(new BankDepositReceived
        {
            Provider = provider,
            BankReference = incoming.BankReference,
            Amount = incoming.Amount,
            Currency = incoming.Currency,
            Description = incoming.Description,
            SenderNationalId = incoming.SenderNationalId,
            ReceivedAt = incoming.ReceivedAt
        }, JsonOptions);

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // "Önce SELECT sonra INSERT" YOK (CLAUDE.md): bildirim ve tarama aynı anda
        // aynı havaleyi getirebilir.
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO bank_deposits
                 (id, provider, bank_reference, amount, currency, description, sender_name, sender_iban,
                  sender_national_id, received_at, discovered_at, discovered_via, payload, publish_attempts)
             VALUES
                 ({id}, {provider}, {incoming.BankReference}, {incoming.Amount}, {incoming.Currency},
                  {incoming.Description}, {incoming.SenderName}, {incoming.SenderIban}, {incoming.SenderNationalId},
                  {incoming.ReceivedAt}, {now}, {via}, {payload}, 0)
             ON CONFLICT (provider, bank_reference) DO NOTHING
             """,
            ct);

        if (inserted == 0)
        {
            logger.LogDebug(
                "Havale zaten kayıtlı, yok sayılıyor. {Provider}/{BankReference} ({Via})",
                provider, incoming.BankReference, via);

            return false;
        }

        // Kişisel veri log'a YAZILMIYOR: ne açıklama ne gönderenin bilgileri.
        logger.LogInformation(
            "Havale kaydedildi. {Provider}/{BankReference}, {Amount} {Currency} ({Via})",
            provider, incoming.BankReference, incoming.Amount, incoming.Currency, via);

        return true;
    }
}

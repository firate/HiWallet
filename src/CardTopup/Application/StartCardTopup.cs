using System.Net;
using System.Text.Json;
using HiWallet.CardTopup.Domain;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.CardTopup.Infrastructure.Upstream;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HiWallet.CardTopup.Application;

/// <param name="Subject">İsteyen kimlik (<c>sub</c>); idempotency kapsamı.</param>
public sealed record StartCardTopupCommand(
    string Subject, string IdempotencyKey, Guid WalletId, decimal Amount, string Currency, string ReturnUrl);

/// <param name="Replayed">Bu anahtarla yükleme zaten başlatılmıştı; yenisi açılmadı.</param>
public sealed record StartCardTopupResult(Domain.CardTopup CardTopup, bool Replayed);

/// <summary>
/// Kartla yüklemeyi başlatır: kayıt, wallet'tan limit payı, sağlayıcıda ödeme. Üç adım
/// SIRAYLA ve her biri ayrı commit:
/// <list type="number">
/// <item><b>Kayıt önce.</b> Pay istenmeden yükleme kalıcı: süreç pay alındıktan sonra ölse
/// bile pay sahipsiz kalmıyor, tarama yüklemeyi bulup kapatıyor.</item>
/// <item><b>Pay ödemeden önce.</b> Limit yetmiyorsa ödeme hiç açılmıyor ve kart çekilmiyor;
/// wallet'ın reddi istemciye AYNEN dönüyor.</item>
/// <item><b>Ödeme en son.</b> Sağlayıcı aynı referansla ikinci ödeme açmıyor; cevabı kaybolan
/// açılış aynı anahtarla tekrar edilen istekte yeniden deneniyor.</item>
/// </list>
///
/// <b>Tekrar eden istek kaldığı yerden devam ediyor.</b> Her adım tekrar edilebilir (pay ve
/// ödeme yüklemenin kimliğiyle tekil), dolayısıyla cevabı alınamayan bir istek aynı anahtarla
/// yeniden gönderildiğinde yarım kalan iş tamamlanıyor.
/// </summary>
public sealed class StartCardTopupHandler(
    IDbContextFactory<CardTopupDbContext> contextFactory,
    CardTopupTransitions transitions,
    WalletHoldClient wallet,
    CardPaymentClient payments,
    IOptions<CardTopupOptions> options,
    TimeProvider timeProvider,
    ILogger<StartCardTopupHandler> logger)
{
    public async Task<StartCardTopupResult> HandleAsync(StartCardTopupCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;

        var topup = Domain.CardTopup.Start(
            id: Guid.NewGuid(),
            subject: command.Subject,
            idempotencyKey: command.IdempotencyKey,
            walletId: command.WalletId,
            amount: command.Amount,
            currency: command.Currency,
            provider: settings.Provider!,
            returnUrl: command.ReturnUrl,
            expiresAt: now + settings.SessionLifetime,
            now: now);

        var replayed = !await TryInsertAsync(topup, ct);

        if (replayed)
        {
            topup = await ReadAsync(command.Subject, command.IdempotencyKey, ct);
        }

        if (topup.State is CardTopupState.Created)
        {
            topup = await PlaceHoldAsync(topup, ct);
        }

        // Oturumu kapanmış yüklemede ödeme açılmıyor: sağlayıcı geçmiş bitişi reddeder ve
        // payı tarama kapatıyor.
        if (topup.State is CardTopupState.Pending && topup.PaymentId is null && topup.ExpiresAt > timeProvider.GetUtcNow())
        {
            topup = await OpenPaymentAsync(topup, ct);
        }

        return new StartCardTopupResult(topup, replayed);
    }

    /// <returns>Yazıldıysa <c>true</c>; bu anahtarla yükleme zaten varsa <c>false</c>.</returns>
    private async Task<bool> TryInsertAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        db.CardTopups.Add(topup);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            // "Önce SELECT sonra INSERT" YOK (CLAUDE.md "Idempotency"): tekilliğe veritabanı
            // karar veriyor.
            logger.LogInformation(
                "Kartla yükleme isteği tekrar; mevcut yükleme dönülüyor. Anahtar {Key}", topup.IdempotencyKey);

            return false;
        }
    }

    private async Task<Domain.CardTopup> ReadAsync(string subject, string idempotencyKey, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.CardTopups
            .AsNoTracking()
            .SingleAsync(c => c.Subject == subject && c.IdempotencyKey == idempotencyKey, ct);
    }

    private async Task<Domain.CardTopup> PlaceHoldAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        PlacedHold hold;

        try
        {
            hold = await wallet.PlaceAsync(topup, ct);
        }
        catch (WalletRejectedException rejected) when (IsRefusal(rejected.StatusCode))
        {
            // Wallet payı vermedi ve hiçbir şey yazmadı. Yükleme kapanıyor, ret istemciye
            // aynen gidiyor.
            var reason = ReasonOf(rejected);

            await transitions.ApplyAsync(topup.Id, (t, now) => t.HoldRejected(reason, now), ct);

            logger.LogInformation("Kartla yükleme wallet'ta reddedildi: {Reason}. {CardTopupId}", reason, topup.Id);

            throw;
        }

        var (current, result) = await transitions.ApplyAsync(topup.Id, (t, now) => t.HoldPlaced(hold.AccountId, now), ct);

        if (result is TransitionResult.Conflict)
        {
            // Pay yazıldı ama yükleme bu arada kapandı (tarama terk edilmiş saydı ya da eşzamanlı
            // bir tekrar reddedildi). Pay kendiliğinden kapanmaz; ödenmedi kapanışı gönderiliyor.
            logger.LogWarning(
                "Kapanmış kartla yüklemenin payı geç yazıldı, serbest bırakılıyor. {CardTopupId} [{State}]",
                current.Id, current.State.ToText());

            await transitions.ReleaseLateHoldAsync(current, ct);
        }

        return current;
    }

    private async Task<Domain.CardTopup> OpenPaymentAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        ProviderPayment payment;

        try
        {
            payment = await payments.OpenAsync(topup, ct);
        }
        catch (ProviderRejectedException exception)
        {
            // Sağlayıcı isteğimizi reddetti; aynı istek aynı sonucu verir. Yükleme ödenmedi diye
            // kapanıyor, pay serbest kalıyor.
            logger.LogError(exception, "Kart sağlayıcısı ödemeyi açmadı. {CardTopupId}", topup.Id);

            var (failed, _) = await transitions.ApplyAsync(
                topup.Id, (t, now) => t.Failed(FailureReasons.ProviderRejected, now), ct);

            return failed;
        }

        var (current, _) = await transitions.ApplyAsync(
            topup.Id, (t, now) => t.PaymentOpened(payment.Id, payment.PaymentUrl, now), ct);

        return current;
    }

    /// <summary>
    /// Wallet'ın kararı olan ret: girdi geçersiz, cüzdan yok ya da kural izin vermiyor.
    /// Kimlik ve yetki retleri (401, 403) ve çakışma bunun dışında: yükleme açık kalıyor,
    /// tarama kapatıyor.
    /// </summary>
    private static bool IsRefusal(HttpStatusCode status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity;

    /// <summary>
    /// Kaydedilen sebep. <c>422</c>'nin gövdesinde wallet'ın kural adı var
    /// (<c>card_topup_limit</c>, ...); arayüz mesajı ondan seçiyor.
    /// </summary>
    private static string ReasonOf(WalletRejectedException rejected)
    {
        if (rejected.StatusCode is HttpStatusCode.NotFound) return "wallet_not_found";
        if (rejected.StatusCode is HttpStatusCode.BadRequest) return "invalid_request";

        try
        {
            using var body = JsonDocument.Parse(rejected.Body);

            if (body.RootElement.TryGetProperty("rule", out var rule) && rule.ValueKind is JsonValueKind.String)
            {
                return rule.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Gövde ProblemDetails değil; genel sebep yeterli.
        }

        return "business_rule";
    }

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "ux_card_topups_idempotency";
}

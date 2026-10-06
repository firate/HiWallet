using HiWallet.CardTopup.Domain;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.CardTopup.Infrastructure.Upstream;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HiWallet.CardTopup.Application;

/// <param name="Abandoned">Payı onaylanmadan terk edilip kapatılanlar.</param>
/// <param name="Closed">Sağlayıcıya sorulup kapatılanlar.</param>
/// <param name="Unresolved">
/// Oturumu kapandığı halde sağlayıcının hâlâ açık ya da tanınmayan bir durum söylediği
/// yüklemeler. Payları duruyor; alarm.
/// </param>
public sealed record CardTopupScanReport(int Abandoned, int Closed, int Unresolved);

/// <summary>
/// Açık yüklemeleri kapatır. Zamanlamadan ayrı duruyor ki test beklemek zorunda kalmasın.
/// <list type="bullet">
/// <item><b>Terk edilmiş (<c>created</c>):</b> pay isteği cevapsız kaldı ve kimse yeniden
/// denemedi. Ödeme hiç açılmadı (pay onaylanmadan açılmıyor); sağlayıcıya sorulmadan ödenmedi
/// diye kapanıyor. Pay yazılmamışsa wallet kapanışı zararsızca atlıyor.</item>
/// <item><b>Oturumu kapanmış (<c>pending</c>):</b> sağlayıcı süresi dolan ödemeyi BİLDİRMİYOR;
/// sonucu sorarak öğreniliyor. Bildirimi kaçırılmış ödeme de burada bulunuyor. Oturum
/// kapandıktan sonra sağlayıcı ödeme kabul etmediği için cevap kesin.</item>
/// </list>
///
/// Pay SAATLE DÜŞMÜYOR: yükleme yalnızca sağlayıcının kesin cevabıyla kapanıyor. Sağlayıcıya
/// ulaşılamazsa yükleme açık kalıyor ve sonraki turda yeniden soruluyor.
/// </summary>
public sealed class OpenCardTopupScanner(
    IDbContextFactory<CardTopupDbContext> contextFactory,
    CardTopupTransitions transitions,
    CardPaymentClient payments,
    TimeProvider timeProvider,
    IOptions<CardTopupOptions> options,
    ILogger<OpenCardTopupScanner> logger)
{
    private readonly CardTopupScanOptions _options = options.Value.Scan;

    public async Task<CardTopupScanReport> ScanAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var abandoned = 0;

        foreach (var id in await AbandonedAsync(now - _options.CreatedStaleAfter, ct))
        {
            var (_, result) = await transitions.ApplyAsync(id, (t, at) => t.Failed(FailureReasons.Abandoned, at), ct);

            if (result is TransitionResult.Applied) abandoned++;
        }

        var closed = 0;
        var unresolved = 0;

        foreach (var id in await ExpiredAsync(now - _options.ExpiryGrace, ct))
        {
            ProviderPayment? payment;

            try
            {
                payment = await payments.FindAsync(id, ct);
            }
            catch (UpstreamUnavailableException exception)
            {
                // Sağlayıcı cevap vermiyor; turun kalanı da vermeyecek. Yüklemeler açık kalıyor.
                logger.LogWarning(exception, "Kart sağlayıcısına ulaşılamadı, tarama sonraki turda devam edecek.");
                break;
            }

            var (topup, result) = await transitions.ApplyAsync(id, (t, at) => Close(t, payment, at), ct);

            if (result is TransitionResult.Applied)
            {
                closed++;
            }
            else if (!topup.IsTerminal)
            {
                unresolved++;

                logger.LogError(
                    "Oturumu kapanmış kartla yükleme kapatılamadı. {CardTopupId}, sağlayıcının durumu {Status}",
                    id, payment?.Status ?? "yok");
            }
        }

        return new CardTopupScanReport(abandoned, closed, unresolved);
    }

    /// <summary>
    /// Sağlayıcının cevabını geçişe çevirir. Tanınmayan ya da hâlâ açık durum kararı
    /// ERTELİYOR: "ödenmedi" saymak çekilmiş bir kartın parasını kaybettirir, "ödendi" saymak
    /// çekilmemiş parayı cüzdana yazardı.
    /// </summary>
    private static TransitionResult Close(Domain.CardTopup topup, ProviderPayment? payment, DateTimeOffset now)
    {
        if (payment is null) return topup.Failed(FailureReasons.PaymentNotOpened, now);

        return payment.Status switch
        {
            ProviderPaymentStatus.Succeeded => topup.Paid(payment.Id, payment.Amount, payment.Currency, now),
            ProviderPaymentStatus.Canceled => topup.Failed(FailureReasons.Canceled, now),
            ProviderPaymentStatus.Expired => topup.Failed(FailureReasons.Expired, now),
            _ => TransitionResult.Ignored
        };
    }

    private async Task<List<Guid>> AbandonedAsync(DateTimeOffset createdBefore, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.CardTopups
            .AsNoTracking()
            .Where(c => c.State == CardTopupState.Created && c.CreatedAt < createdBefore)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Id)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
    }

    private async Task<List<Guid>> ExpiredAsync(DateTimeOffset expiredBefore, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        return await db.CardTopups
            .AsNoTracking()
            .Where(c => c.State == CardTopupState.Pending && c.ExpiresAt < expiredBefore)
            .OrderBy(c => c.ExpiresAt)
            .Select(c => c.Id)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
    }
}

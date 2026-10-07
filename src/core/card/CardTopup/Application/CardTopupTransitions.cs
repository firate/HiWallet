using HiWallet.CardTopup.Domain;
using HiWallet.CardTopup.Infrastructure.Persistence;
using HiWallet.Shared.Contracts.CardTopups;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.CardTopup.Application;

/// <summary>Bu kimlikte kartla yükleme yok.</summary>
public sealed class CardTopupNotFoundException(Guid cardTopupId)
    : Exception($"Kartla yükleme bulunamadı: {cardTopupId}")
{
    public Guid CardTopupId { get; } = cardTopupId;
}

/// <summary>
/// Yüklemeye bir geçiş uygular ve kaydeder. Başlatma, sağlayıcının bildirimi ve tarama aynı
/// yoldan geçiyor.
///
/// <b>Kapanış geçişle AYNI transaction'da outbox'a yazılıyor.</b> Yükleme ödendi ya da
/// ödenmedi diye kapandığında wallet'a gidecek <see cref="CardTopupClosed"/> o commit'in
/// parçası; ayrı commit olsaydı aradaki çökme payı ya kalıcı bırakır ya da parayı cüzdana
/// hiç ulaştırmazdı.
///
/// <b>Çakışmada taze okumayla yeniden deneniyor.</b> Bildirim ile tarama aynı yüklemeye aynı
/// anda gelebiliyor; ikincisinin geçişi yeni durum üstünde yeniden değerlendiriliyor.
/// </summary>
public sealed class CardTopupTransitions(
    IDbContextFactory<CardTopupDbContext> contextFactory,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 3;

    /// <param name="closedAt">
    /// Kapanışın sağlayıcı tarafındaki anı; bildirimde olayın anı. Verilmezse şimdi.
    /// </param>
    /// <exception cref="CardTopupNotFoundException">Bu kimlikte yükleme yok.</exception>
    public async Task<(Domain.CardTopup CardTopup, TransitionResult Result)> ApplyAsync(
        Guid cardTopupId,
        Func<Domain.CardTopup, DateTimeOffset, TransitionResult> transition,
        CancellationToken ct,
        DateTimeOffset? closedAt = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct);

            var topup = await db.CardTopups.FirstOrDefaultAsync(c => c.Id == cardTopupId, ct)
                        ?? throw new CardTopupNotFoundException(cardTopupId);

            var now = timeProvider.GetUtcNow();
            var result = transition(topup, now);

            if (result is not TransitionResult.Applied)
            {
                return (topup, result);
            }

            if (topup.State is CardTopupState.Paid or CardTopupState.Failed)
            {
                db.Outbox.Add(OutboxMessage.For(Closure(topup, closedAt ?? now), now));
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return (topup, result);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Başka bir yazar yetişti; geçiş yeni durumla yeniden değerlendirilecek.
            }
        }
    }

    /// <summary>
    /// Durumu değişmeyen yükleme için ödenmedi kapanışı. Pay isteği yükleme reddedildikten ya
    /// da terk edildikten SONRA cevap verdiyse wallet'ta bir pay var ve onu kapatacak başka bir
    /// şey yok. Pay yoksa wallet kapanışı zararsızca atlıyor; varsa serbest bırakıyor.
    /// </summary>
    public async Task ReleaseLateHoldAsync(Domain.CardTopup topup, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        db.Outbox.Add(OutboxMessage.For(Closure(topup, CardTopupClosedOutcomes.Failed, now), now));

        await db.SaveChangesAsync(ct);
    }

    private static CardTopupClosed Closure(Domain.CardTopup topup, DateTimeOffset closedAt) =>
        Closure(
            topup,
            topup.State is CardTopupState.Paid ? CardTopupClosedOutcomes.Paid : CardTopupClosedOutcomes.Failed,
            closedAt);

    /// <remarks>
    /// Tutar, para birimi ve sağlayıcı yüklemenin kaydından: wallet onları kendi payıyla
    /// karşılaştırıyor. Ödeme hiç açılmadıysa sağlayıcının referansı boş.
    /// </remarks>
    private static CardTopupClosed Closure(Domain.CardTopup topup, string outcome, DateTimeOffset closedAt) => new()
    {
        CardTopupId = topup.Id,
        Outcome = outcome,
        Provider = topup.Provider,
        ProviderRef = topup.PaymentId ?? string.Empty,
        Amount = topup.Amount,
        Currency = topup.Currency,
        ClosedAt = closedAt
    };
}

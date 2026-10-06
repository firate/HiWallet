using HiWallet.CardTopup.Domain;
using HiWallet.Shared.Contracts.CardPayments;

namespace HiWallet.CardTopup.Application;

/// <summary>Bildirimin tipi tanınmıyor: sağlayıcının sözleşmesi değişmiş olabilir.</summary>
public sealed class UnknownPaymentEventException(string type)
    : Exception($"Tanınmayan ödeme bildirimi: {type}");

/// <summary>
/// Sağlayıcının ödeme bildirimini yüklemeye uygular. Kart çekildiyse yükleme ödendi, müşteri
/// vazgeçtiyse ödenmedi diye kapanıyor; kapanış aynı commit'te wallet'a yola çıkıyor.
///
/// <b>Bildirim yüklemenin kaydıyla eşleşmek zorunda:</b> sağlayıcı, ödeme kimliği, tutar ve
/// para birimi. Eşleşmeyen bildirim <see cref="TransitionResult.Conflict"/>: yükleme
/// değişmiyor, alarm üretiliyor ve tüketici mesajı dead-letter'a yolluyor.
/// </summary>
public sealed class ApplyCardPaymentHandler(
    CardTopupTransitions transitions,
    ILogger<ApplyCardPaymentHandler> logger)
{
    /// <exception cref="UnknownPaymentEventException">Tip tanınmıyor.</exception>
    /// <exception cref="CardTopupNotFoundException">Referansta yükleme yok: bizim açmadığımız bir ödeme.</exception>
    public async Task<TransitionResult> HandleAsync(CardPaymentUpdated message, CancellationToken ct)
    {
        if (!CardPaymentEvents.IsKnown(message.Type))
        {
            throw new UnknownPaymentEventException(message.Type);
        }

        var (topup, result) = await transitions.ApplyAsync(
            message.Reference,
            (t, now) => Apply(t, message, now),
            ct,
            closedAt: message.OccurredAt);

        if (result is TransitionResult.Conflict)
        {
            // Error, Warning DEĞİL: kart çekilmiş ama yükleme kapanmış ya da sağlayıcı kaydımızla
            // uyuşmayan bir şey bildiriyor. Kendiliğinden çözülmüyor.
            logger.LogError(
                "Ödeme bildirimi kartla yüklemeyle çelişiyor. {CardTopupId} [{State}], bildirim {Type} " +
                "{PaymentId} {Amount} {Currency} ({Provider})",
                topup.Id, topup.State.ToText(), message.Type, message.PaymentId,
                message.Amount, message.Currency, message.Provider);
        }

        return result;
    }

    private static TransitionResult Apply(Domain.CardTopup topup, CardPaymentUpdated message, DateTimeOffset now)
    {
        if (topup.Provider != message.Provider) return TransitionResult.Conflict;
        if (topup.PaymentId is not null && topup.PaymentId != message.PaymentId) return TransitionResult.Conflict;

        return message.Type is CardPaymentEvents.Succeeded
            ? topup.Paid(message.PaymentId, message.Amount, message.Currency, now)
            : topup.Failed(FailureReasons.Canceled, now);
    }
}

using System.Collections.Concurrent;

namespace HiWallet.Stripe.Fake.Payments;

/// <summary>
/// Açılmış ödemeler, bellekte. Referans başına tek ödeme: aynı referansla ikinci istek ilk
/// ödemeyi dönüyor, gerçek sağlayıcıların idempotency anahtarı gibi.
/// </summary>
public sealed class PaymentStore(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, CardPayment> _byId = new();
    private readonly ConcurrentDictionary<Guid, CardPayment> _byReference = new();

    /// <returns>Ödeme ve yeni açılıp açılmadığı.</returns>
    public (CardPayment Payment, bool Created) Open(
        Guid reference, decimal amount, string currency, string returnUrl, DateTimeOffset expiresAt)
    {
        var created = false;

        var payment = _byReference.GetOrAdd(reference, _ =>
        {
            created = true;

            return new CardPayment
            {
                Id = $"pay_{Guid.NewGuid():N}",
                Reference = reference,
                Amount = amount,
                Currency = currency,
                ReturnUrl = returnUrl,
                ExpiresAt = expiresAt,
                CreatedAt = time.GetUtcNow()
            };
        });

        _byId.TryAdd(payment.Id, payment);

        return (payment, created);
    }

    public CardPayment? Find(string id) => _byId.GetValueOrDefault(id);

    public CardPayment? FindByReference(Guid reference) => _byReference.GetValueOrDefault(reference);

    /// <summary>
    /// Müşterinin kararını yazar. Yalnızca açık ve süresi dolmamış ödemede: karar verilmiş ya
    /// da süresi dolmuş ödeme değişmiyor.
    /// </summary>
    /// <returns>Karar yazıldıysa <c>true</c>.</returns>
    public bool Decide(CardPayment payment, PaymentStatus decision)
    {
        lock (payment)
        {
            var now = time.GetUtcNow();

            if (payment.StatusAt(now) is not PaymentStatus.RequiresPayment)
            {
                return false;
            }

            payment.Decision = decision;
            payment.DecidedAt = now;

            return true;
        }
    }
}

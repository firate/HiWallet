namespace HiWallet.Stripe.Fake.Payments;

/// <summary>Durumların ve webhook olaylarının sözleşmedeki metni.</summary>
public static class PaymentText
{
    public const string Succeeded = "payment.succeeded";

    public const string Canceled = "payment.canceled";

    public static string Of(PaymentStatus status) => status switch
    {
        PaymentStatus.RequiresPayment => "requires_payment",
        PaymentStatus.Succeeded => "succeeded",
        PaymentStatus.Canceled => "canceled",
        PaymentStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Eşlemesi yazılmamış durum.")
    };
}

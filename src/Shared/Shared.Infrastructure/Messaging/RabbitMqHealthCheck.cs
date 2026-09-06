using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Bağlantı açık mı değil, KANAL AÇILABİLİYOR MU diye bakıyor. Açık görünen bir
/// bağlantı üstünde kanal açmak broker'a gerçek bir tur attırıyor; sadece
/// <c>IsOpen</c> okumak istemcinin kendi görüşünü doğrulamak olurdu.
/// </summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    /// <summary>
    /// Kontrolün cevap vermek için kendine tanıdığı süre.
    ///
    /// Kayıttaki <c>timeout</c> YETMİYOR: o yalnızca token'ı iptal ediyor ve
    /// <c>HealthCheckService</c> görevi terk etmiyor — alttaki istemci token'ı
    /// gözlemlemezse probe asılı kalıyor. Broker durdurulunca ölçülen süre 5 sn'ydi
    /// (istemcinin recovery aralığı), compose'un probe timeout'u ise 3 sn. Sonuç:
    /// bilerek <c>Degraded</c> diyen servis <c>unhealthy</c> işaretleniyordu ve
    /// broker'sız çalışabilme tasarımı kâğıt üstünde kalıyordu.
    ///
    /// Süre burada ZORLANIYOR: bekleme bırakılıyor, alttaki deneme arka planda
    /// bitiyor. Sağlık kontrolü cevabı geciktirmemeli, işi bitirmek zorunda değil.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        try
        {
            // WaitAsync yalnızca BEKLEMEYİ iptal ediyor; asıl çağrıya iptal token'ı
            // ayrıca geçiliyor ki istemci gözlemleyebiliyorsa gerçekten dursun.
            var current = await connection.GetAsync(cancellationToken)
                .AsTask()
                .WaitAsync(deadline.Token);

            await using var channel = await current
                .CreateChannelAsync(cancellationToken: cancellationToken)
                .WaitAsync(deadline.Token);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: $"Broker {Deadline.TotalSeconds:0} saniyede cevap vermedi.");
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: exception);
        }
    }
}

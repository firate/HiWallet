using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiWallet.Shared.Infrastructure.Messaging;

/// <summary>
/// Bağlantı açık mı değil, KANAL AÇILABİLİYOR MU diye bakıyor. Açık görünen bir
/// bağlantı üstünde kanal açmak broker'a gerçek bir tur attırıyor; sadece
/// <c>IsOpen</c> okumak istemcinin kendi görüşünü doğrulamak olurdu.
/// </summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await connection.GetAsync(cancellationToken);
            await using var channel = await current.CreateChannelAsync(cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: exception);
        }
    }
}

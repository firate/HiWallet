using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HiWallet.Shared.Infrastructure.Jobs;

/// <summary>
/// Periyodik iş. Quartz.NET yerine <see cref="PeriodicTimer"/>: cron ifadesine,
/// kalıcı zamanlayıcı deposuna ve kümelemeye ihtiyaç yok — tekilliği zaten
/// <see cref="JobLease"/> çözüyor (<c>decisions.md</c> madde 3).
///
/// <b>İlk tur beklemeden koşmuyor.</b> <see cref="PeriodicTimer"/> önce süreyi
/// bekliyor; bu istenen davranış: dağıtımda ayağa kalkan her instance aynı anda
/// tarama başlatmıyor.
///
/// <b>Turlar üst üste binmiyor.</b> Bir tur süreyi aşarsa sonraki tick kaçırılıyor,
/// kuyruk oluşmuyor. Aynı işin iki turunun paralel koşması bu job'ların hiçbirinde
/// anlamlı değil.
///
/// <b>Graceful shutdown.</b> SIGTERM'de <c>stoppingToken</c> iptal ediliyor ve tur
/// yarıda kalıyor; iş <see cref="CancellationToken"/>'a saygı duymak zorunda
/// (<c>baseline.md</c> madde 10).
/// </summary>
public abstract class ScheduledJob(JobLease lease, ILogger logger) : BackgroundService
{
    /// <summary>Kilit anahtarı bundan türüyor; instance'lar arasında AYNI olmalı.</summary>
    protected abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        logger.LogInformation("{Job} her {Interval} sürede bir koşacak.", Name, Interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await lease.TryRunAsync(Name, RunAsync, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Bir turun hatası job'ı öldürmüyor. Öldürseydi tarama sessizce
                // durur ve durduğu ancak aylar sonra fark edilirdi.
                logger.LogError(exception, "{Job} turu başarısız, sonraki turda yeniden denenecek.", Name);
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

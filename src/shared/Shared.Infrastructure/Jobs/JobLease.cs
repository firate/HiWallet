using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HiWallet.Shared.Infrastructure.Jobs;

/// <summary>
/// Zamanlanmış işi aynı anda tek instance'ın koşmasını sağlar
/// (<c>decisions.md</c> madde 3): <c>pg_try_advisory_lock</c>, alamayan o turu atlar.
///
/// <b>Redis distributed lock YOK.</b> Kilit zaten yazacağı veritabanında duruyor,
/// yani ikinci bir altyapı parçasına bağımlılık yok ve TTL sorunu yok — bağlantı
/// koparsa Postgres kilidi kendisi bırakıyor. Redis'te süresi dolan bir kilit işi
/// yarıda bırakılmış instance'ın üstüne ikinci bir instance salardı.
///
/// <b>Kilit işin bağlantısında DEĞİL.</b> Buradaki bağlantı yalnızca kilidi tutuyor;
/// job kendi <c>DbContext</c>'iyle, kendi bağlantısında çalışıyor. Kilit ile işin
/// transaction'ını aynı yere bağlamak işin transaction sınırlarını kilide bağımlı
/// yapardı — job artık kendi commit'ini istediği gibi bölemezdi.
/// </summary>
public sealed class JobLease(string connectionString, ILogger<JobLease> logger)
{
    /// <summary>
    /// İş koştuysa <c>true</c>, kilit başkasındaysa <c>false</c>. Kilit alınamaması
    /// hata DEĞİL: başka bir instance aynı turu koşuyor demek, beklenen durum.
    /// </summary>
    public async Task<bool> TryRunAsync(
        string jobName, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        var key = KeyFor(jobName);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection))
        {
            acquire.Parameters.AddWithValue(key);

            if (await acquire.ExecuteScalarAsync(ct) is not true)
            {
                logger.LogDebug("{Job} kilidi başka bir instance'ta, bu tur atlanıyor.", jobName);
                return false;
            }
        }

        try
        {
            await work(ct);
            return true;
        }
        finally
        {
            // Bağlantı havuza dönerken Npgsql zaten DISCARD ALL gönderiyor ve kilit
            // düşüyor. Yine de açıkça bırakılıyor: davranışı havuz ayarına bağlı
            // bırakmak, ayar değiştiğinde kilidi sonsuza kadar tutardı.
            await ReleaseAsync(connection, key, jobName);
        }
    }

    private async Task ReleaseAsync(NpgsqlConnection connection, long key, string jobName)
    {
        try
        {
            // İptal token'ı GEÇİLMİYOR: kapanış sırasında iptal edilmiş bir token'la
            // çağrılırsa kilit bırakılmadan çıkardık.
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection);
            release.Parameters.AddWithValue(key);

            await release.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Bağlantı zaten kopmuşsa kilit sunucu tarafında düşmüştür; bu bir uyarı,
            // hata değil.
            logger.LogWarning(exception, "{Job} kilidi açıkça bırakılamadı.", jobName);
        }
    }

    /// <summary>
    /// İş adından kilit anahtarı. <c>string.GetHashCode()</c> KULLANILAMAZ: .NET'te
    /// süreç başına rastgeleleştiriliyor, yani iki instance aynı iş için farklı
    /// anahtar üretir ve kilit hiçbir şeyi engellemez. Sessiz ve ancak çift koşan
    /// bir job'ın sonucundan anlaşılan bir kusur olurdu.
    /// </summary>
    internal static long KeyFor(string jobName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(jobName));

        return BitConverter.ToInt64(hash, 0);
    }
}

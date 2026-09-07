using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Topups;

/// <summary>
/// Relay tekilliği (<c>decisions.md</c> madde 30).
///
/// Kilidin varlık sebebi SIRALAMA: iki relay ayrı batch'ler alıp farklı hızda
/// yayınlarsa aynı cüzdanın iki event'i exchange'e ters sırada varıyor ve kuyruğun
/// içindeki <c>x-single-active-consumer</c> garantisi bunu düzeltmiyor —
/// o, kuyruğa YANLIŞ SIRADA gelmiş mesajı düzeltmez.
///
/// Sıranın kendisini uçtan uca sınamak gerçek bir yarış kurgulamayı gerektirirdi ve
/// sonucu zamana bağlı olurdu. Sınanan şey mekanizma: kilit gerçekten karşılıklı
/// dışlama sağlıyor mu ve relay onu kullanıyor mu.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RelaySingleInstanceTests(InboxFixture inbox)
{
    /// <summary>
    /// Relay'in kullandığı iş adı. Sabit burada TEKRARLANIYOR ve bu bilinçli: test
    /// kodun kullandığı anahtarı doğrulamak zorunda. Aynı sabiti paylaşsalardı isim
    /// değiştiğinde test de sessizce onunla birlikte kayar ve hiçbir şey kanıtlamazdı.
    /// </summary>
    private const string RelayJobName = "topup:relay";

    private JobLease CreateLease() =>
        new(inbox.ConnectionString, NullLogger<JobLease>.Instance);

    /// <summary>
    /// <b>Asıl kanıt.</b> Kilit tutulurken ikinci bir tur koşamıyor. Relay turunu
    /// bu kilidin altında çalıştırdığı için, ikinci instance o turda inbox'a hiç
    /// bakmıyor.
    /// </summary>
    [Fact]
    public async Task KilitTutulurken_IkinciTurKosamaz()
    {
        var ct = TestContext.Current.CancellationToken;

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var holder = CreateLease().TryRunAsync(RelayJobName, async _ =>
        {
            started.SetResult();
            await release.Task;
        }, ct);

        await started.Task;

        var ranWhileHeld = await CreateLease().TryRunAsync(
            RelayJobName, _ => Task.CompletedTask, ct);

        release.SetResult();
        (await holder).ShouldBeTrue();

        ranWhileHeld.ShouldBeFalse("kilit tutulurken ikinci relay turu koşmamalı");
    }

    /// <summary>
    /// Kilit relay'in KENDİ veritabanında. Ortak bir kilit veritabanı, servis
    /// sınırını (madde 7) delen bir bağımlılık olurdu; kilit yalnızca aynı servisin
    /// instance'ları arasında anlamlı.
    /// </summary>
    [Fact]
    public async Task Kilit_InboxVeritabaninda()
    {
        var ct = TestContext.Current.CancellationToken;

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var holder = CreateLease().TryRunAsync(RelayJobName, async _ =>
        {
            started.SetResult();
            await release.Task;
        }, ct);

        await started.Task;

        // Kilit inbox veritabanının oturumunda duruyor; pg_locks üzerinden görünür.
        await using (var db = inbox.CreateContext())
        {
            var held = await db.Database
                .SqlQuery<long>(
                    $"""
                     SELECT ((classid::bigint << 32) | objid::bigint) AS "Value"
                     FROM pg_locks
                     WHERE locktype = 'advisory'
                     """)
                .ToListAsync(ct);

            held.ShouldContain(JobLease.KeyFor(RelayJobName));
        }

        release.SetResult();
        await holder;
    }

    /// <summary>
    /// Tur bittiğinde kilit bırakılıyor. Bırakılmasaydı relay ilk turdan sonra bir
    /// daha hiç yayın yapmazdı — ve bu, hiçbir hata üretmeden, mesajlar inbox'ta
    /// birikerek olurdu.
    /// </summary>
    [Fact]
    public async Task TurBittikten_SonraKilitBirakilir()
    {
        var ct = TestContext.Current.CancellationToken;

        await CreateLease().TryRunAsync(RelayJobName, _ => Task.CompletedTask, ct);

        (await CreateLease().TryRunAsync(RelayJobName, _ => Task.CompletedTask, ct))
            .ShouldBeTrue("bir sonraki tur kilidi alabilmeli");
    }
}

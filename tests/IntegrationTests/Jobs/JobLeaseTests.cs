using HiWallet.IntegrationTests.Fixtures;
using HiWallet.Shared.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace HiWallet.IntegrationTests.Jobs;

/// <summary>
/// Job tekilliği (<c>decisions.md</c> madde 3). Kilit gerçek Postgres'e karşı
/// sınanıyor — <c>pg_try_advisory_lock</c>'un davranışı taklit edilemez.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JobLeaseTests(OrchestratorFixture orchestrator)
{
    private JobLease CreateLease() =>
        new(orchestrator.ConnectionString, NullLogger<JobLease>.Instance);

    /// <summary>
    /// Asıl kanıt: iki instance aynı anda koştuğunda iş BİR kez yapılıyor.
    /// Kilit olmasaydı takılmış saga taraması iki kez alarm üretirdi; ileride
    /// aynı altyapıyı kullanacak fatura işleme job'ında (5.6) bedeli çok daha
    /// ağır — aynı fatura iki kez ledger'a yazılırdı.
    /// </summary>
    [Fact]
    public async Task IkiInstance_IsiTekSeferKosturur()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobName = $"test:tekillik:{Guid.NewGuid():N}";

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var runs = 0;

        // Birincisi kilidi tutarken ikincisi deniyor.
        var first = CreateLease().TryRunAsync(jobName, async _ =>
        {
            Interlocked.Increment(ref runs);
            started.SetResult();
            await release.Task;
        }, ct);

        await started.Task;

        var second = await CreateLease().TryRunAsync(
            jobName, _ => { Interlocked.Increment(ref runs); return Task.CompletedTask; }, ct);

        release.SetResult();

        (await first).ShouldBeTrue("kilidi ilk alan iş koşmalıydı");
        second.ShouldBeFalse("kilit başkasındayken ikinci instance koşmamalıydı");
        runs.ShouldBe(1);
    }

    /// <summary>
    /// Kilit bırakılıyor mu. Bırakılmasaydı ilk turdan sonra job bir daha hiç
    /// koşmazdı — ve bu, hiçbir hata üretmeden sessizce olurdu.
    /// </summary>
    [Fact]
    public async Task IsBittikten_SonraKilitBirakilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobName = $"test:birakma:{Guid.NewGuid():N}";

        await CreateLease().TryRunAsync(jobName, _ => Task.CompletedTask, ct);
        var second = await CreateLease().TryRunAsync(jobName, _ => Task.CompletedTask, ct);

        second.ShouldBeTrue("önceki tur bittiğine göre kilit serbest olmalıydı");
    }

    /// <summary>
    /// İş patlarsa da kilit bırakılmalı. Aksi halde tek bir hatalı tur job'ı
    /// kalıcı olarak susturur.
    /// </summary>
    [Fact]
    public async Task IsPatlarsa_KilitYineDeBirakilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobName = $"test:hata:{Guid.NewGuid():N}";

        await Should.ThrowAsync<InvalidOperationException>(
            CreateLease().TryRunAsync(
                jobName, _ => throw new InvalidOperationException("tur patladı"), ct));

        var second = await CreateLease().TryRunAsync(jobName, _ => Task.CompletedTask, ct);

        second.ShouldBeTrue("hatalı tur kilidi kalıcı olarak tutmamalı");
    }

    /// <summary>
    /// Farklı işler birbirini engellemiyor.
    /// </summary>
    [Fact]
    public async Task FarkliIsler_BirbiriniEngellemez()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = CreateLease().TryRunAsync($"test:a:{suffix}", async _ =>
        {
            started.SetResult();
            await release.Task;
        }, ct);

        await started.Task;

        var second = await CreateLease().TryRunAsync(
            $"test:b:{suffix}", _ => Task.CompletedTask, ct);

        release.SetResult();
        await first;

        second.ShouldBeTrue();
    }

    /// <summary>
    /// Anahtar süreçten sürece AYNI olmalı. <c>string.GetHashCode()</c> .NET'te
    /// süreç başına rastgeleleştiriliyor; kullanılsaydı iki instance farklı anahtar
    /// üretir ve kilit hiçbir şeyi engellemezdi. Sabit değer, hash'in değiştiğini
    /// de yakalar — değişirse dağıtım sırasında eski ve yeni sürüm birbirini
    /// göremez ve job kısa süreliğine çift koşar.
    /// </summary>
    [Fact]
    public void Anahtar_IsAdindanDeterministikUretilir()
    {
        JobLease.KeyFor("withdrawal:stuck-saga-scan")
            .ShouldBe(JobLease.KeyFor("withdrawal:stuck-saga-scan"));

        JobLease.KeyFor("withdrawal:stuck-saga-scan")
            .ShouldNotBe(JobLease.KeyFor("wallet:business-summary"));
    }
}

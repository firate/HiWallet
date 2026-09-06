using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace HiWallet.IntegrationTests.Jobs;

/// <summary>
/// Takılmış saga taraması (adım 5.2, <c>decisions.md</c> madde 33).
///
/// Bu taramanın yakaladığı durum hiçbir hata log'u üretmiyor: wallet parayı düşmüş
/// ama cevabı kaybolmuşsa saga <c>debited</c>'da kalıyor, banka komutu
/// tüketilmemişse <c>bank_transfer_pending</c>'de. Sistem sağlıklı görünüyor,
/// müşterinin parası görünmüyor.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StuckSagaScannerTests(OrchestratorFixture fixture)
{
    private static readonly Iban Destination = Iban.From("TR330006100519786457841326");
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(15);

    private StuckSagaScanner CreateScanner(int sampleSize = 20) =>
        new(
            fixture.ContextFactory,
            new FixedTimeProvider(Now),
            Options.Create(new StuckSagaScanOptions { Threshold = Threshold, SampleSize = sampleSize }));

    [Fact]
    public async Task EsigiAsanAktifSaga_Raporlanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var stuck = NewSaga(startedAt: Now - Threshold - TimeSpan.FromMinutes(1));

        await SaveAsync(ct, stuck);

        var report = await CreateScanner().ScanAsync(ct);

        report.Oldest.Select(s => s.SagaId).ShouldContain(stuck.Id);
    }

    /// <summary>
    /// Eşiğin altındaki saga raporlanmamalı. Normal bir çekim saniyeler sürüyor;
    /// mutlu yolu alarma çevirmek alarmı değersizleştirirdi.
    /// </summary>
    [Fact]
    public async Task EsiginAltindakiSaga_Raporlanmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var fresh = NewSaga(startedAt: Now - TimeSpan.FromMinutes(1));

        await SaveAsync(ct, fresh);

        var report = await CreateScanner().ScanAsync(ct);

        report.Oldest.Select(s => s.SagaId).ShouldNotContain(fresh.Id);
    }

    /// <summary>
    /// Terminal durumlar taranmıyor. Durum listesi <c>WithdrawalStates.Active</c>'ten
    /// türüyor; elle yazılsaydı yeni bir terminal durum eklendiğinde tarama bitmiş
    /// çekimleri "takılmış" diye raporlamaya başlardı.
    /// </summary>
    [Theory]
    [InlineData(WithdrawalState.Completed)]
    [InlineData(WithdrawalState.Rejected)]
    [InlineData(WithdrawalState.Failed)]
    public async Task TerminalSaga_EskiOlsaDaRaporlanmaz(WithdrawalState terminal)
    {
        var ct = TestContext.Current.CancellationToken;
        var old = Now - Threshold - TimeSpan.FromHours(1);
        var saga = NewSaga(startedAt: old);

        DriveToTerminal(saga, terminal, old);
        saga.State.ShouldBe(terminal, "test kurulumu hedef duruma ulaşmalıydı");

        await SaveAsync(ct, saga);

        var report = await CreateScanner().ScanAsync(ct);

        report.Oldest.Select(s => s.SagaId).ShouldNotContain(saga.Id);
    }

    /// <summary>
    /// <c>Total</c> örneklemle sınırlı DEĞİL. Alarm "20'den fazla" demek yerine kaç
    /// tane olduğunu söylemeli — yaygın bir kesintide aradaki fark, olayın
    /// büyüklüğünü anlamakla anlamamak arasındaki fark.
    /// </summary>
    [Fact]
    public async Task Toplam_OrneklemSinirindanBagimsiz()
    {
        var ct = TestContext.Current.CancellationToken;
        var old = Now - Threshold - TimeSpan.FromMinutes(5);

        var sagas = Enumerable.Range(0, 5).Select(_ => NewSaga(startedAt: old)).ToArray();
        await SaveAsync(ct, sagas);

        var report = await CreateScanner(sampleSize: 2).ScanAsync(ct);

        report.Oldest.Count.ShouldBe(2);
        report.Total.ShouldBeGreaterThanOrEqualTo(5);
    }

    private async Task SaveAsync(CancellationToken ct, params WithdrawalSaga[] sagas)
    {
        await using var db = fixture.CreateContext();
        db.Sagas.AddRange(sagas);
        await db.SaveChangesAsync(ct);
    }

    private static void DriveToTerminal(WithdrawalSaga saga, WithdrawalState terminal, DateTimeOffset at)
    {
        if (terminal is WithdrawalState.Rejected)
        {
            saga.Rejected("yetersiz bakiye", at);
            return;
        }

        saga.Debited(Guid.NewGuid(), 102m, at);
        saga.BankTransferStarted(Guid.NewGuid(), at);

        if (terminal is WithdrawalState.Completed)
        {
            saga.BankTransferSucceeded(at);
            return;
        }

        saga.BankTransferFailed("banka reddetti", at);
        saga.Refunded(Guid.NewGuid(), at);
    }

    private static WithdrawalSaga NewSaga(DateTimeOffset startedAt) =>
        WithdrawalSaga.Start(
            id: Guid.NewGuid(),
            accountId: Guid.NewGuid(),
            walletId: Guid.NewGuid(),
            amount: 100m,
            currency: "TRY",
            destination: Destination,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            startedAt: startedAt);
}

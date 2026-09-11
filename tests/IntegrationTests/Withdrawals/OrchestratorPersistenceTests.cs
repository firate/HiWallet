using HiWallet.Shared.Contracts.Actors;
using HiWallet.IntegrationTests.Fixtures;
using HiWallet.WithdrawalOrchestrator.Domain;
using HiWallet.WithdrawalOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HiWallet.IntegrationTests.Withdrawals;

/// <summary>
/// Orchestrator'ın kalıcılığı: saga satırı, outbox ve ikisinin AYNI transaction'da
/// olması (decisions.md madde 32).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrchestratorPersistenceTests(OrchestratorFixture fixture)
{
    private static readonly Iban Destination = Iban.From("TR330006100519786457841326");

    [Fact]
    public async Task Saga_KaydedilipGeriOkunur()
    {
        var saga = NewSaga();

        await using (var db = fixture.CreateContext())
        {
            db.Sagas.Add(saga);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateContext();
        var stored = await read.Sagas.SingleAsync(
            s => s.Id == saga.Id, TestContext.Current.CancellationToken);

        stored.AccountId.ShouldBe(saga.AccountId);
        stored.WalletId.ShouldBe(saga.WalletId);
        stored.Amount.ShouldBe(250.75m);
        stored.Currency.ShouldBe("TRY");
        stored.State.ShouldBe(WithdrawalState.Initiated);
        stored.Version.ShouldBe(0);

        // Değer tipi geri okunurken yeniden doğrulanıyor; bozuk bir satır burada patlar.
        stored.Destination.Value.ShouldBe(Destination.Value);

        // Hiç para hareketi olmadan NULL kalmalı — 0 yazmak "komisyonsuz çekim
        // yapıldı" ile ayırt edilemezdi.
        stored.TotalDebited.ShouldBeNull();
        stored.DebitTransactionId.ShouldBeNull();
    }

    /// <summary>
    /// Durum operasyonun elle bakacağı bir alan; <c>state = 4</c> bir insana hiçbir
    /// şey söylemiyor. Eşlemenin sessizce int'e dönmediğini doğruluyor.
    /// </summary>
    [Fact]
    public async Task Durum_VeritabaninaOkunabilirMetinOlarakYaziliyor()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 252.75m, DateTimeOffset.UtcNow);

        await using (var db = fixture.CreateContext())
        {
            db.Sagas.Add(saga);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateContext();
        var state = await read.Database
            .SqlQuery<string>($"SELECT state AS \"Value\" FROM withdrawal_sagas WHERE id = {saga.Id}")
            .SingleAsync(TestContext.Current.CancellationToken);

        state.ShouldBe("debited");
    }

    [Fact]
    public async Task AyniHesapVeAnahtar_IkinciKezYazilamaz()
    {
        var accountId = Guid.NewGuid();
        const string key = "cift-istek";

        await using (var db = fixture.CreateContext())
        {
            db.Sagas.Add(NewSaga(accountId: accountId, idempotencyKey: key));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var second = fixture.CreateContext();
        second.Sagas.Add(NewSaga(accountId: accountId, idempotencyKey: key));

        var exception = await Should.ThrowAsync<DbUpdateException>(
            () => second.SaveChangesAsync(TestContext.Current.CancellationToken));

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe("ux_withdrawal_sagas_idempotency");
    }

    /// <summary>
    /// Idempotency kapsamı HESAP. Yalnız key UNIQUE olsaydı iki müşterinin aynı
    /// anahtarı üretmesi birinin çekimini sessizce yutardı (decisions.md madde 4).
    /// </summary>
    [Fact]
    public async Task FarkliHesap_AyniAnahtar_Yazilabilir()
    {
        const string key = "ayni-anahtar";

        await using var db = fixture.CreateContext();

        db.Sagas.Add(NewSaga(accountId: Guid.NewGuid(), idempotencyKey: key));
        db.Sagas.Add(NewSaga(accountId: Guid.NewGuid(), idempotencyKey: key));

        await Should.NotThrowAsync(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Aynı saga'ya iki yazar aynı anda gelebiliyor (banka cevabı + takılmış saga
    /// taraması). Token olmadan ikincisi birincisinin geçişini sessizce ezerdi
    /// (decisions.md madde 2).
    /// </summary>
    [Fact]
    public async Task EskiSurumleGuncelleme_Cakisma_Uretir()
    {
        var saga = NewSaga();

        await using (var setup = fixture.CreateContext())
        {
            setup.Sagas.Add(saga);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // İki bağımsız okuyucu, ikisi de version 0 görüyor.
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        var firstCopy = await first.Sagas.SingleAsync(
            s => s.Id == saga.Id, TestContext.Current.CancellationToken);
        var secondCopy = await second.Sagas.SingleAsync(
            s => s.Id == saga.Id, TestContext.Current.CancellationToken);

        firstCopy.Debited(Guid.NewGuid(), 252.75m, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        secondCopy.Rejected("limit aşıldı", DateTimeOffset.UtcNow);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync(TestContext.Current.CancellationToken));

        // Kaybeden yazar hiçbir iz bırakmamalı: durum ilk yazarın bıraktığı yerde.
        await using var read = fixture.CreateContext();
        var stored = await read.Sagas.SingleAsync(
            s => s.Id == saga.Id, TestContext.Current.CancellationToken);

        stored.State.ShouldBe(WithdrawalState.Debited);
        stored.Version.ShouldBe(1);
    }

    /// <summary>
    /// Madde 32'nin kendisi: geçiş ile komut tek commit'te kalıcı oluyor.
    /// </summary>
    [Fact]
    public async Task GecisVeOutbox_TekSaveChangesteKaliciOlur()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 252.75m, DateTimeOffset.UtcNow);

        var commandId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Sagas.Add(saga);
            db.Outbox.Add(NewOutboxMessage(commandId, saga.Id));

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateContext();

        var stored = await read.Sagas.SingleAsync(
            s => s.Id == saga.Id, TestContext.Current.CancellationToken);
        var message = await read.Outbox.SingleAsync(
            m => m.Id == commandId, TestContext.Current.CancellationToken);

        stored.State.ShouldBe(WithdrawalState.Debited);
        message.SagaId.ShouldBe(saga.Id);
        message.RoutingKey.ShouldBe("StartBankTransfer");

        // Relay henüz dokunmadı.
        message.PublishedAt.ShouldBeNull();
        message.PublishAttempts.ShouldBe(0);
    }

    /// <summary>
    /// Madde 32'nin asıl koruduğu senaryo. Commit başarısız olduğunda saga
    /// ilerlememeli AMA komut da ortada kalmamalı — tersi olsaydı bankaya komut
    /// gider, saga hiç ilerlememiş görünürdü.
    /// </summary>
    [Fact]
    public async Task CommitBasarisizOlursa_NeGecisNeKomutKalir()
    {
        var accountId = Guid.NewGuid();
        const string key = "cakisan-anahtar";

        await using (var setup = fixture.CreateContext())
        {
            setup.Sagas.Add(NewSaga(accountId: accountId, idempotencyKey: key));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // İkinci saga aynı anahtarla; unique index yüzünden commit patlayacak.
        var doomed = NewSaga(accountId: accountId, idempotencyKey: key);
        var commandId = Guid.NewGuid();

        await using (var db = fixture.CreateContext())
        {
            db.Sagas.Add(doomed);
            db.Outbox.Add(NewOutboxMessage(commandId, doomed.Id));

            await Should.ThrowAsync<DbUpdateException>(
                () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using var read = fixture.CreateContext();

        (await read.Sagas.AnyAsync(s => s.Id == doomed.Id, TestContext.Current.CancellationToken))
            .ShouldBeFalse();

        // Kritik olan bu: komut geride kalsaydı relay onu yayınlar ve karşılığı
        // olmayan bir banka transferi başlardı.
        (await read.Outbox.AnyAsync(m => m.Id == commandId, TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Kısmi index filtresi <see cref="WithdrawalStates.Active"/>'ten üretiliyor.
    /// Bu test ikisinin ayrışmadığını doğruluyor: yeni bir terminal durum eklenip
    /// migration alınmazsa takılmış saga taraması bitmiş saga'ları raporlamaya başlar.
    /// </summary>
    [Fact]
    public async Task AktifSagaIndexi_TerminalDurumlariDISARIDA_Birakir()
    {
        await using var db = fixture.CreateContext();

        var definition = await db.Database
            .SqlQuery<string>(
                $"""
                 SELECT indexdef AS "Value"
                   FROM pg_indexes
                  WHERE schemaname = {fixture.Schema}
                    AND indexname = 'ix_withdrawal_sagas_active'
                 """)
            .SingleAsync(TestContext.Current.CancellationToken);

        foreach (var state in WithdrawalStates.Active)
        {
            definition.ShouldContain($"'{state.ToText()}'");
        }

        foreach (var state in Enum.GetValues<WithdrawalState>().Where(s => s.IsTerminal()))
        {
            definition.ShouldNotContain($"'{state.ToText()}'");
        }
    }

    private static WithdrawalSaga NewSaga(Guid? accountId = null, string? idempotencyKey = null) =>
        WithdrawalSaga.Start(
            id: Guid.NewGuid(),
            accountId: accountId ?? Guid.NewGuid(),
            walletId: Guid.NewGuid(),
            amount: 250.75m,
            currency: "TRY",
            destination: Destination,
            idempotencyKey: idempotencyKey ?? Guid.NewGuid().ToString("N"),
            initiatedBy: new CommandActor { Type = ActorTypes.Customer, Id = Guid.NewGuid().ToString() },
            startedAt: DateTimeOffset.UtcNow);

    private static OutboxMessage NewOutboxMessage(Guid commandId, Guid sagaId) => new()
    {
        Id = commandId,
        SagaId = sagaId,
        RoutingKey = "StartBankTransfer",
        Payload = $$"""{"commandId":"{{commandId}}","sagaId":"{{sagaId}}"}""",
        CreatedAt = DateTimeOffset.UtcNow
    };
}

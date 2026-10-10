using HiWallet.Shared.Contracts.Actors;
using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.UnitTests.DepositReturns;

/// <summary>
/// Askıdaki havalenin iadesinin durum makinesi. Saf: DB, mesajlaşma ve zaman bilmiyor.
/// Çekim saga'sıyla aynı ayrım: zararsız tekrar yok sayılıyor, para kaybına işaret eden
/// çelişki durumu değiştirmiyor.
/// </summary>
public sealed class DepositReturnSagaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static DepositReturnSaga NewSaga() => DepositReturnSaga.Start(
        Guid.NewGuid(), suspendedDepositId: Guid.NewGuid(), requestedBy: "calisan-1", idempotencyKey: "istek-1", Now);

    private static TransitionResult Debit(DepositReturnSaga saga, Guid? bankCommandId = null) =>
        saga.Debited(Guid.NewGuid(), 250m, "TRY", "bank-fake", "GLN0001", bankCommandId ?? Guid.NewGuid(), Now);

    [Fact]
    public void YeniSaga_Initiated_AktoruIsteyenCalisan()
    {
        var saga = NewSaga();

        saga.State.ShouldBe(DepositReturnState.Initiated);
        saga.IsTerminal.ShouldBeFalse();
        saga.RequesterActor().ShouldBe(new CommandActor { Type = ActorTypes.Employee, Id = "calisan-1" });
    }

    [Fact]
    public void IsteyenVeAnahtarOlmadanAcilmaz()
    {
        Should.Throw<ArgumentException>(() =>
            DepositReturnSaga.Start(Guid.NewGuid(), Guid.NewGuid(), requestedBy: " ", "istek-1", Now));
        Should.Throw<ArgumentException>(() =>
            DepositReturnSaga.Start(Guid.NewGuid(), Guid.NewGuid(), "calisan-1", idempotencyKey: "", Now));
    }

    /// <summary>
    /// Askıdan düşülünce banka komutu aynı geçişte üretiliyor: saga bankayı bekliyor ve
    /// komutun kimliği saga'da. Tutar ve havalenin bankası wallet'ın cevabından geliyor.
    /// </summary>
    [Fact]
    public void MutluYol_Dusuldu_Bankada_Kapaniyor_Tamamlandi()
    {
        var saga = NewSaga();
        var bankCommand = Guid.NewGuid();

        Debit(saga, bankCommand).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.BankTransferPending);
        saga.BankCommandId.ShouldBe(bankCommand);
        saga.Amount.ShouldBe(250m);
        saga.Provider.ShouldBe("bank-fake");
        saga.DepositBankReference.ShouldBe("GLN0001");

        saga.BankTransferSucceeded("IADE-1", 1.5m, Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.Settling);
        saga.BankFee.ShouldBe(1.5m);

        saga.Settled(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.Completed);
        saga.IsTerminal.ShouldBeTrue();
    }

    /// <summary>Banka reddederse para askıya geri konuyor; havale yeniden karara açılıyor.</summary>
    [Fact]
    public void BankaReddi_AskiyaGeriKonuyor_Basarisiz()
    {
        var saga = NewSaga();
        Debit(saga);

        saga.BankTransferFailed("Hesap kapalı", Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.Restoring);
        saga.FailureRule.ShouldBe(DepositReturnFailureRules.BankRejected);

        saga.Restored(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.Failed);
        saga.IsTerminal.ShouldBeTrue();
    }

    /// <summary>Havale yok, aktarılmış ya da iadesi sürüyor: hiçbir para hareketi olmadı.</summary>
    [Fact]
    public void WalletReddi_YalnizcaDusulmedenOnce()
    {
        var saga = NewSaga();

        saga.Rejected("Aktarılmış", "deposit_already_resolved", Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(DepositReturnState.Rejected);
        saga.FailureRule.ShouldBe("deposit_already_resolved");
        saga.Rejected("Aktarılmış", "deposit_already_resolved", Now).ShouldBe(TransitionResult.Ignored);

        var debited = NewSaga();
        Debit(debited);
        debited.Rejected("Geç gelen ret", "deposit_already_resolved", Now).ShouldBe(TransitionResult.Conflict);
        debited.State.ShouldBe(DepositReturnState.BankTransferPending);
    }

    [Fact]
    public void TekrarEdenEventler_YokSayiliyor_ZamanDegismiyor()
    {
        var saga = NewSaga();
        Debit(saga);
        var version = saga.Version;

        Debit(saga).ShouldBe(TransitionResult.Ignored);
        saga.Version.ShouldBe(version);

        saga.BankTransferSucceeded("IADE-1", 0m, Now);
        saga.BankTransferSucceeded("IADE-1", 0m, Now).ShouldBe(TransitionResult.Ignored);
    }

    /// <summary>
    /// Askıya geri konduktan sonra gelen "başarılı" para hem göndericiye gitmiş hem askıya
    /// dönmüş olabilir demek: durum değişmiyor, alarm.
    /// </summary>
    [Fact]
    public void GeriKonduktanSonraBasarili_Celiski()
    {
        var saga = NewSaga();
        Debit(saga);
        saga.BankTransferFailed("Hesap kapalı", Now);

        saga.BankTransferSucceeded("IADE-1", 0m, Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(DepositReturnState.Restoring);

        var settling = NewSaga();
        Debit(settling);
        settling.BankTransferSucceeded("IADE-1", 0m, Now);
        settling.BankTransferFailed("Geç gelen ret", Now).ShouldBe(TransitionResult.Conflict);
        settling.State.ShouldBe(DepositReturnState.Settling);
    }

    [Fact]
    public void DurumAdlari_IkiYonde_Ayni()
    {
        foreach (var state in Enum.GetValues<DepositReturnState>())
        {
            DepositReturnStates.FromText(state.ToText()).ShouldBe(state);
        }

        DepositReturnStates.Active.ShouldBe(
            [DepositReturnState.Initiated, DepositReturnState.BankTransferPending, DepositReturnState.Settling, DepositReturnState.Restoring],
            ignoreOrder: true);
    }
}

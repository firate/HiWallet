using HiWallet.WithdrawalOrchestrator.Domain;

namespace HiWallet.UnitTests.Withdrawals;

/// <summary>
/// Saga state machine'i. Saf — DB yok, mesajlaşma yok (overview.md madde 6).
///
/// Sınanan asıl şey geçişlerin kendisi değil, <b>tekrar gelen event'lerin
/// sınıflandırılması</b>: hangi tekrar zararsız, hangisi para kaybına işaret ediyor.
/// </summary>
public sealed class WithdrawalSagaTests
{
    private static readonly Iban Destination = Iban.From("TR330006100519786457841326");

    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static WithdrawalSaga NewSaga() => WithdrawalSaga.Start(
        Guid.NewGuid(),
        accountId: Guid.NewGuid(),
        walletId: Guid.NewGuid(),
        amount: 100m,
        currency: "TRY",
        destination: Destination,
        idempotencyKey: "istek-1",
        startedAt: Now);

    [Fact]
    public void YeniSaga_InitiatedDurumunda_Baslar()
    {
        var saga = NewSaga();

        saga.State.ShouldBe(WithdrawalState.Initiated);
        saga.IsTerminal.ShouldBeFalse();
    }

    // ------------------------------------------------------------------
    // Mutlu yol
    // ------------------------------------------------------------------

    [Fact]
    public void MutluYol_Initiated_Debited_Pending_Completed()
    {
        var saga = NewSaga();

        saga.Debited(Guid.NewGuid(), totalDebited: 102m, Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.Debited);

        saga.BankTransferStarted(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.BankTransferPending);

        // Banka başarılı dedi ama saga daha BİTMİYOR: clearing hâlâ dolu ve
        // muhasebenin kapanması gerekiyor (adım 5.5b). Burada bitirseydi settlement
        // kaydını yazacak bir şey kalmaz, terminal saga'yı takılmış saga taraması da
        // görmez ve clearing sessizce açık kalırdı.
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.Settling);
        saga.IsTerminal.ShouldBeFalse();
        saga.BankFee.ShouldBe(1.50m);
        saga.BankReference.ShouldBe("BNK-1");

        saga.Settled(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.Completed);
        saga.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void TelafiYolu_Pending_Compensating_Failed()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);

        saga.BankTransferFailed("hesap kapalı", Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.Compensating);
        saga.FailureReason.ShouldBe("hesap kapalı");

        saga.Refunded(Guid.NewGuid(), Now).ShouldBe(TransitionResult.Applied);
        saga.State.ShouldBe(WithdrawalState.Failed);
        saga.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Reddedilme_ParaHareketiOlmadan_Terminal()
    {
        var saga = NewSaga();

        saga.Rejected("günlük limit aşıldı", Now).ShouldBe(TransitionResult.Applied);

        saga.State.ShouldBe(WithdrawalState.Rejected);
        saga.IsTerminal.ShouldBeTrue();

        // Hiç para hareketi olmadı: debit kaydı yok.
        saga.DebitTransactionId.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Zararsız tekrarlar — broker en az bir kez teslim ediyor
    // ------------------------------------------------------------------

    [Fact]
    public void AyniEventIkiKez_IkincisiYokSayilir()
    {
        var saga = NewSaga();
        var transactionId = Guid.NewGuid();

        saga.Debited(transactionId, 102m, Now).ShouldBe(TransitionResult.Applied);
        saga.Debited(transactionId, 102m, Now).ShouldBe(TransitionResult.Ignored);

        // Durum ilerlememiş, ilk sonuç korunmuş.
        saga.State.ShouldBe(WithdrawalState.Debited);
        saga.DebitTransactionId.ShouldBe(transactionId);
    }

    [Fact]
    public void GecmisteKalanEvent_YokSayilir()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);

        // Saga ilerledi; geciken debit event'i geri sarmamalı.
        saga.Debited(Guid.NewGuid(), 102m, Now).ShouldBe(TransitionResult.Ignored);
        saga.State.ShouldBe(WithdrawalState.BankTransferPending);
    }

    [Fact]
    public void TamamlanmisSagaya_TekrarBasari_YokSayilir()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now);
        saga.Settled(Guid.NewGuid(), Now);

        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Ignored);
        saga.State.ShouldBe(WithdrawalState.Completed);
    }

    // ------------------------------------------------------------------
    // Çelişkili event'ler — sessizce yutulmamalı
    // ------------------------------------------------------------------

    [Fact]
    public void TelafiSirasindaBasariHaberi_CELISKI()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);
        saga.BankTransferFailed("timeout", Now);

        // Banka "olmadı" dedi, biz iade ediyoruz, sonra "oldu" diyor. Para hem
        // bankadan çıkmış hem müşteriye geri verilmiş olabilir — bu bir mutabakat
        // vakası. "Yok say" demek zararı görünmez kılardı.
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Conflict);

        // Durum DEĞİŞMİYOR: telafi yarıda bırakılmaz, insan bakana kadar devam eder.
        saga.State.ShouldBe(WithdrawalState.Compensating);
    }

    [Fact]
    public void IadeEdildiktenSonraBasariHaberi_CELISKI()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);
        saga.BankTransferFailed("timeout", Now);
        saga.Refunded(Guid.NewGuid(), Now);

        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(WithdrawalState.Failed);
    }

    [Fact]
    public void TamamlanmisSagaya_BasarisizlikHaberi_CELISKI()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);
        saga.BankTransferStarted(Guid.NewGuid(), Now);
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now);
        saga.Settled(Guid.NewGuid(), Now);

        saga.BankTransferFailed("geç gelen hata", Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(WithdrawalState.Completed);
    }

    [Fact]
    public void HicDusulmemisken_BankaBasarisi_CELISKI()
    {
        var saga = NewSaga();

        // Cüzdandan para düşmeden banka transferi başarılı olamaz. Olduysa ya
        // event yanlış saga'ya geldi ya da sıralama bozuldu.
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(WithdrawalState.Initiated);
    }

    [Fact]
    public void ReddedilmisSagaya_HerhangiBirIlerleme_CELISKI()
    {
        var saga = NewSaga();
        saga.Rejected("limit", Now);

        // Reddedilen istekte bankaya hiç komut gitmedi; bir cevap gelmesi mümkün değil.
        saga.Debited(Guid.NewGuid(), 102m, Now).ShouldBe(TransitionResult.Conflict);
        saga.BankTransferSucceeded("BNK-1", 1.50m, Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(WithdrawalState.Rejected);
    }

    [Fact]
    public void TerminalSagaya_Reddetme_CELISKI()
    {
        var saga = NewSaga();
        saga.Debited(Guid.NewGuid(), 102m, Now);

        // Para düştükten sonra "reddedildi" demek anlamsız; reddetme yalnızca
        // hiçbir hareket olmamışken geçerli.
        saga.Rejected("geç kalan limit kontrolü", Now).ShouldBe(TransitionResult.Conflict);
        saga.State.ShouldBe(WithdrawalState.Debited);
    }

    // ------------------------------------------------------------------
    // Kayıt tutma
    // ------------------------------------------------------------------

    [Fact]
    public void HerGecis_UpdatedAtiIlerletir_YokSayilanIlerletmez()
    {
        var saga = NewSaga();
        var later = Now.AddMinutes(5);

        saga.Debited(Guid.NewGuid(), 102m, later);
        saga.UpdatedAt.ShouldBe(later);

        var evenLater = later.AddMinutes(5);
        saga.Debited(Guid.NewGuid(), 102m, evenLater).ShouldBe(TransitionResult.Ignored);

        // Yok sayılan event durumu değiştirmediği gibi zaman damgasına da
        // dokunmamalı; aksi halde "en son ne zaman ilerledi" bilgisi bozulur.
        saga.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void DusulenToplam_KomisyonDahil_Saklanir()
    {
        var saga = NewSaga();

        // Komisyonu wallet hesaplıyor; orchestrator yalnızca sonucu kaydediyor.
        // Politikayı iki serviste tekrarlamamak için bu yönde akıyor.
        saga.Debited(Guid.NewGuid(), totalDebited: 102m, Now);

        saga.Amount.ShouldBe(100m);
        saga.TotalDebited.ShouldBe(102m);
    }
}

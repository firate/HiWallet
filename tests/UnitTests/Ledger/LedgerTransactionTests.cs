using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;

namespace HiWallet.UnitTests.Ledger;

public sealed class LedgerTransactionTests
{
    private static readonly Currency Try = Currency.From("TRY");
    private static readonly Currency Usd = Currency.From("USD");

    private static readonly Guid Sender = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Receiver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Revenue = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static LedgerTransaction NewTransaction(LedgerTransactionType type = LedgerTransactionType.P2P)
    {
        return LedgerTransaction.Create(
            Guid.NewGuid(), type, Sender, Actor.Customer(Sender), DateTimeOffset.UnixEpoch, "test-key");
    }

    [Fact]
    public void AssertBalanced_ToplamiSifir_Gecer()
    {
        var tx = NewTransaction()
            .AddEntry(Sender, new Money(-100m, Try))
            .AddEntry(Receiver, new Money(100m, Try));

        Should.NotThrow(() => tx.AssertBalanced());
    }

    [Fact]
    public void AssertBalanced_KomisyonluUcBacak_Gecer()
    {
        // payment örneği: 100 ödeme, 2 komisyon (overview.md madde 4).
        var tx = NewTransaction(LedgerTransactionType.Payment)
            .AddEntry(Sender, new Money(-102m, Try))
            .AddEntry(Receiver, new Money(100m, Try))
            .AddEntry(Revenue, new Money(2m, Try));

        Should.NotThrow(() => tx.AssertBalanced());
    }

    [Fact]
    public void AssertBalanced_ToplamSifirDegil_Patlar()
    {
        var tx = NewTransaction()
            .AddEntry(Sender, new Money(-100m, Try))
            .AddEntry(Receiver, new Money(50m, Try));

        Should.Throw<UnbalancedLedgerTransactionException>(() => tx.AssertBalanced());
    }

    [Fact]
    public void AssertBalanced_KarisikParaBirimiToplamSifir_PATLAR()
    {
        // Para basma senaryosu: tek SUM sıfır çıkıyor ama işlem dengeli DEĞİL.
        // DB trigger'ı da GROUP BY currency ile aynı soruyu soruyor (decisions.md madde 17).
        var tx = NewTransaction()
            .AddEntry(Sender, new Money(100m, Try))
            .AddEntry(Receiver, new Money(-100m, Usd));

        Should.Throw<UnbalancedLedgerTransactionException>(() => tx.AssertBalanced());
    }

    [Fact]
    public void AssertBalanced_TekBacak_Patlar()
    {
        var tx = NewTransaction().AddEntry(Sender, new Money(0.01m, Try));

        Should.Throw<UnbalancedLedgerTransactionException>(() => tx.AssertBalanced());
    }

    [Fact]
    public void AddEntry_SifirTutar_Reddeder()
    {
        var tx = NewTransaction();

        Should.Throw<ArgumentException>(() => tx.AddEntry(Sender, Money.Zero(Try)));
    }

    [Fact]
    public void Create_IdempotencyKapsamiBos_Reddeder()
    {
        // account_id nullable olsaydı unique index'teki NULL'lar eşleşmez ve aynı fatura
        // iki kez yazılabilirdi (decisions.md madde 15).
        Should.Throw<ArgumentException>(() => LedgerTransaction.Create(
            Guid.NewGuid(), LedgerTransactionType.ProviderInvoice, Guid.Empty,
            SystemActors.ProviderInvoice, DateTimeOffset.UnixEpoch, "test-key"));
    }

    /// <summary>
    /// Aktör varsayılanı YOK ve boş geçilemiyor (decisions.md madde 34). Varsayılan
    /// olsaydı yeni bir handler onu sessizce devralır ve kalıcı kayda yanlış aktör
    /// yazardı — hiçbir test de kırılmazdı.
    /// </summary>
    [Fact]
    public void Create_AktorBos_Reddeder()
    {
        Should.Throw<ArgumentException>(() => LedgerTransaction.Create(
            Guid.NewGuid(), LedgerTransactionType.P2P, Sender, default, DateTimeOffset.UnixEpoch, "test-key"));
    }

    /// <summary>
    /// Anahtarsız ledger işlemi açılamıyor (<c>decisions.md</c> madde 4). Anahtarsız
    /// bir satır deduplike edilemez: tekrarı hiçbir şeye takılmadan ikinci kez
    /// yazılır ve müşteri aynı parayı iki kez gönderir.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_AnahtarBos_Reddeder(string key)
    {
        Should.Throw<ArgumentException>(() => LedgerTransaction.Create(
            Guid.NewGuid(), LedgerTransactionType.P2P, Sender,
            Actor.Customer(Sender), DateTimeOffset.UnixEpoch, key));
    }

    [Fact]
    public void Create_AktoruKaydeder()
    {
        var tx = LedgerTransaction.Create(
            Guid.NewGuid(), LedgerTransactionType.P2P, Sender,
            Actor.Employee("kc-sub-123"), DateTimeOffset.UnixEpoch, "test-key");

        tx.ActorType.ShouldBe(ActorType.Employee);
        tx.ActorId.ShouldBe("kc-sub-123");
    }

    [Fact]
    public void UnbalancedException_DomainExceptionDenTuremez()
    {
        // Dengesiz işlem bir iş kuralı reddi DEĞİL, koda giren hata: 422 değil 500.
        typeof(UnbalancedLedgerTransactionException).IsAssignableTo(typeof(DomainException)).ShouldBeFalse();
    }
}

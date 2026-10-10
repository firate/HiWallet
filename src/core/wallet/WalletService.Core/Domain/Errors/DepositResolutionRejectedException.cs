namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Askıdaki havale için verilen karar kurala uymuyor: havale zaten çözülmüş ya da iadesi
/// sürüyor, hedef hesap havalenin cüzdana geçme kuralına uymuyor ya da anahtar başka bir
/// işlemde kullanılmış. İş kuralı reddi → <c>422</c>.
/// </summary>
public sealed class DepositResolutionRejectedException(string rule, string message) : DomainException(message)
{
    public const string AlreadyResolved = "deposit_already_resolved";

    public const string TargetNotPerson = "deposit_target_not_person";

    public const string KeyReused = "idempotency_key_reused";

    public const string ReturnInProgress = "deposit_return_in_progress";

    /// <summary>İadede havale yok: istek kuyruktan geldiği için 404 değil, ret cevabı.</summary>
    public const string NotFound = "deposit_not_found";

    public string Rule { get; } = rule;
}

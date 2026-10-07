namespace HiWallet.WalletService.Domain.Errors;

/// <summary>Hesap üzerinde iş kuralı reddi → <c>422</c>.</summary>
public sealed class AccountRuleException(string message) : DomainException(message);

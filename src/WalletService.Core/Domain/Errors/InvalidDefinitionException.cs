namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// İstemcinin gönderdiği tanım kendi içinde tutarsız → <c>400</c>, <c>422</c> değil:
/// kuralın izin vermediği bir işlem değil, eksik ya da çelişen bir girdi. Kuralları
/// domain fabrikası tek yerde tutuyor; request doğrulamasında ikinci kopyası yok.
/// </summary>
public sealed class InvalidDefinitionException(string message, Exception inner) : Exception(message, inner);

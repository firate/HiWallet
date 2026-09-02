namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Beklenen iş kuralı reddi. Beklenmeyen hatalardan (unhandled) ayrılır;
/// ProblemDetails eşlemesi bunları tanır (baseline madde 5).
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}

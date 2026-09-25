using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Policies;

namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// Transfer tipi tarafların hesap tipleriyle uyuşmuyor: kişiye <c>Payment</c>,
/// işletmeye <c>P2P</c> gibi.
///
/// Girdi kuralı DEĞİL — request biçimsel olarak kusursuz. Sınırdaki validator bunu
/// bilemez: cevap <c>accounts.type</c>'ta, yani kayıtlı durumda. Bu yüzden
/// <see cref="DomainException"/> ve <c>422</c>.
/// </summary>
public sealed class TransferTypeMismatchException(TransferType type, AccountType sender, AccountType receiver)
    : DomainException(
        $"{type} transferi bu taraflar arasında yapılamaz: gönderen {sender}, alan {receiver}.");

namespace HiWallet.PersonalMobileApi.Requests;

/// <param name="Name">Cüzdan adı ("Birikim"). Aynı para birimindeki cüzdanlar adıyla ayırt ediliyor.</param>
public sealed record OpenWalletRequest(string Name, string Currency);

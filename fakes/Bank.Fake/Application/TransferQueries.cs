using HiWallet.Bank.Fake.Api.Responses;
using HiWallet.Bank.Fake.Infrastructure.Storage;

namespace HiWallet.Bank.Fake.Application;

/// <summary>
/// Transferin okunması. Ayrı sınıf çünkü yazma yolundan farklı: tek bir kayıt
/// okunuyor ve hiçbir şey değişmiyor. Orchestrator'daki
/// <c>WithdrawalQueries</c> ile aynı kalıp — controller depoya doğrudan
/// dokunmuyor.
///
/// <b>Response tipini döndürüyor, entity'yi değil</b> ve bu <c>WithdrawalQueries</c>'ten
/// sapma. Sebebi burada durumun TÜRETİLMİŞ olması: <see cref="TransferResolution"/>
/// zamana bakarak karar veriyor ve o karar tek yerde kalmalı. Entity dönseydi
/// türetmeyi controller yapardı; callback göndericisi de kendi tarafında yapıyor
/// ve ikisi ayrıştığında sahte banka aynı transfer için sorguda başka, callback'te
/// başka şey söylerdi.
/// </summary>
public sealed class TransferQueries(BankFakeStore store, TimeProvider timeProvider)
{
    public TransferStatusResponse? Find(string bankReference)
    {
        BankTransfer? transfer;

        lock (store.Gate)
        {
            store.TransfersByReference.TryGetValue(bankReference, out transfer);
        }

        // Yanıta giren alanların hepsi kabul anında sabitlendi; kilit dışında
        // okunmaları güvenli. Değişen tek alanlar callback'inkiler, onlar burada yok.
        if (transfer is null) return null;

        var status = TransferResolution.StatusOf(transfer, timeProvider.GetUtcNow());

        return new TransferStatusResponse(
            transfer.BankReference,
            transfer.ClientReference,
            status,
            transfer.Amount,
            transfer.Fee,
            transfer.Currency,
            status is TransferStatus.Failed ? TransferResolution.FailureReason : null,
            transfer.AcceptedAt);
    }
}

using System.Threading.Channels;
using FluentValidation;
using HiWallet.Fakes.Topups;

namespace HiWallet.Bank.Fake.Api.Controllers;

/// <summary>
/// Bankanın PARA GİRİŞİ tarafı: müşteri havale/EFT ile yükleme yaptığında banka
/// bize webhook atar.
///
/// <b>Aynı kurum iki yönde de çalışıyor.</b> Bu servis hem gelen havaleyi bildiriyor
/// (burası) hem giden transferi kabul ediyor (<c>TransfersController</c>). Ledger'da
/// da öyle: <c>clearing/bank-fake</c> iki yönde de hareket ediyor, <c>nostro/bank-fake</c>
/// paramızın durduğu yer.
///
/// İki yön FARKLI sözleşme konuşuyor ve bu gerçeğin kendisi: para girişinde bizim
/// formatımıza uyuyor (<c>X-Hive-Signature</c>), çıkış sonucunda kendi formatını
/// dayatıyor (<c>X-Bank-Signature</c>). Bu asimetri projenin bilinen bir
/// basitleştirmesi — gerçekte giriş tarafında da sözleşmeyi banka dayatırdı.
/// </summary>
public sealed class TopupsController(
    Channel<TopupDelivery> queue, IValidator<TopupRequest> validator)
    : TopupsControllerBase(queue, validator);

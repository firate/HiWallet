using System.Threading.Channels;
using FluentValidation;
using HiWallet.Fakes.Topups;

namespace HiWallet.Stripe.Fake.Api.Controllers;

/// <summary>
/// Kart sağlayıcısının para girişi bildirimi. <b>Bu servisin TEK ucu</b> — Stripe'tan
/// para çıkmıyor, dolayısıyla transfer ya da callback tarafı yok.
///
/// Gerçek Stripe'ta bu uç YOKTUR: webhook müşteri kartla ödeme yaptığında gelir,
/// sen istediğinde değil.
/// </summary>
public sealed class TopupsController(
    Channel<TopupDelivery> queue, IValidator<TopupRequest> validator)
    : TopupsControllerBase(queue, validator);

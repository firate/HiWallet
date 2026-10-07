/**
 * Kart sağlayıcısının ödeme sayfası. Uygulamanın dışında: sayfa tamamen değişiyor, müşteri
 * ödemeden sonra sağlayıcının yönlendirmesiyle `/kart-yukleme`'ye dönüyor.
 */
export function openPaymentPage(url: string): void {
  window.location.assign(url)
}

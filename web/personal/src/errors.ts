import { ApiError } from './api'

/**
 * Sunucunun iş kuralı reddi müşterinin diliyle. Anahtar ProblemDetails'teki `rule`.
 * Sunucunun mesajı log ve destek için yazıldı: hesap kimliği ve kuralın iç adını
 * taşıyor, müşteriye gösterilmiyor.
 */
const ruleMessages: Record<string, string> = {
  insufficient_funds: 'Bakiyen bu işleme yetmiyor.',
  transfer_type_mismatch:
    'Gönderim türü alıcıya uymuyor: kişiye "Bir kişiye", işyerine "İşyerine ödeme" seçilerek gönderiliyor.',
  unsupported_currency: 'Şu an yalnızca TRY cüzdanı açılabiliyor.',
  'Kyc.OutgoingTransfer.Monthly': 'Doğrulama seviyenin aylık gönderim limiti bu tutara izin vermiyor.',
  'Kyc.Payment.Monthly': 'Doğrulama seviyenin aylık ödeme limiti bu tutara izin vermiyor.',
  // Alıcının limiti. Hangi limitin dolduğu alıcının hesabı hakkında bilgi; söylenmiyor.
  'Kyc.IncomingTransfer.Monthly': 'Alıcı bu tutarı alamıyor.',
  'Kyc.IncomingTotal.Monthly': 'Alıcı bu tutarı alamıyor.',
  'Kyc.Balance': 'Alıcı bu tutarı alamıyor.',
}

/** Tarifenin işlem ve gün limitleri; adın başı transfer tipi (`P2P.Daily`, `Payment.PerTransaction`). */
function tariffMessage(rule: string): string | undefined {
  if (rule.endsWith('.PerTransaction')) {
    return 'Tutar tek seferde gönderilebilecek tutarı aşıyor.'
  }

  if (rule.endsWith('.Daily')) {
    return 'Bugünkü gönderim limitin bu tutara yetmiyor.'
  }

  return undefined
}

/**
 * Hatanın müşteriye gösterilen metni. Bilinen kuralın kendi metni var; alan hatalarında
 * ayrıntı alanların altında; sunucunun beklenmeyen hatası iç ayrıntı taşımıyor. Geri
 * kalanında sunucunun mesajı olduğu gibi.
 */
export function errorText(error: Error): string {
  if (!(error instanceof ApiError)) {
    return error.message
  }

  const rule = error.problem?.rule

  if (rule) {
    const known = ruleMessages[rule] ?? tariffMessage(rule)

    if (known) {
      return known
    }
  }

  if (error.status === 400 && Object.keys(error.problem?.errors ?? {}).length > 0) {
    return 'Bilgileri kontrol et.'
  }

  if (error.status === 500) {
    return 'Beklenmeyen bir hata oluştu. Biraz sonra tekrar dene.'
  }

  return error.message
}

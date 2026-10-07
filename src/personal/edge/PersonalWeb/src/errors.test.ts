import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import { errorText } from './errors'
import type { ProblemDetails } from './types'

function rejected(status: number, problem: ProblemDetails): ApiError {
  return new ApiError(status, problem)
}

describe('errorText', () => {
  /** Sunucunun mesajı hesap kimliğini ve kuralın iç adını taşıyor; müşteri kendi dilini görüyor. */
  it('bilinen kuralı müşterinin diliyle söylüyor', () => {
    const error = rejected(422, {
      detail: "Hesap 3f2a... için 'Kyc.OutgoingTransfer.Monthly' limiti aşıldı: limit 0 TRY, denenen 100 TRY.",
      rule: 'Kyc.OutgoingTransfer.Monthly',
    })

    expect(errorText(error)).toBe('Doğrulama seviyenin aylık gönderim limiti bu tutara izin vermiyor.')
  })

  it('tarifenin işlem ve gün limitini tipinden bağımsız tanıyor', () => {
    expect(errorText(rejected(422, { rule: 'P2P.PerTransaction' }))).toBe(
      'Tutar tek seferde gönderilebilecek tutarı aşıyor.',
    )
    expect(errorText(rejected(422, { rule: 'Payment.Daily' }))).toBe('Bugünkü gönderim limitin bu tutara yetmiyor.')
  })

  /** Hangi limitin dolduğu alıcının hesabı hakkında bilgi. */
  it('alıcının limitinde hangisi olduğunu söylemiyor', () => {
    expect(errorText(rejected(422, { rule: 'Kyc.Balance' }))).toBe('Alıcı bu tutarı alamıyor.')
    expect(errorText(rejected(422, { rule: 'Kyc.IncomingTotal.Monthly' }))).toBe('Alıcı bu tutarı alamıyor.')
  })

  /** Kartla yüklemede parayı alan da isteyen de müşterinin kendisi: limitin onun olduğu söyleniyor. */
  it('kartla yüklemenin limit reddini müşterinin diliyle söylüyor', () => {
    const error = rejected(422, {
      detail: "Bu yükleme doğrulama seviyenin limitine sığmıyor: 'Kyc.IncomingTotal.Monthly' limiti 5500 TRY.",
      rule: 'card_topup_limit',
    })

    expect(errorText(error)).toBe('Bu yükleme doğrulama seviyenin limitine sığmıyor.')
  })

  it('bilinmeyen kuralda sunucunun mesajını gösteriyor', () => {
    const error = rejected(409, { title: 'Bu e-postayla bir hesap var.', rule: 'email_registered' })

    expect(errorText(error)).toBe('Bu e-postayla bir hesap var.')
  })

  /** Bazı uçların doğrulama cevabında başlık çerçevenin İngilizce metni; ayrıntı alanlarda. */
  it('alan hatasında başlık yerine kontrol etmeyi söylüyor', () => {
    const error = rejected(400, {
      title: 'One or more validation errors occurred.',
      errors: { destinationIban: ['IBAN geçersiz.'] },
    })

    expect(errorText(error)).toBe('Bilgileri kontrol et.')
  })

  it('beklenmeyen hatada iç ayrıntı göstermiyor', () => {
    const error = rejected(500, { title: 'An error occurred while processing your request.' })

    expect(errorText(error)).toBe('Beklenmeyen bir hata oluştu. Biraz sonra tekrar dene.')
  })

  it('ön API ulaşamadığında onun mesajını gösteriyor', () => {
    expect(errorText(rejected(503, { title: 'Servis geçici olarak kullanılamıyor' }))).toBe(
      'Servis geçici olarak kullanılamıyor',
    )
  })
})

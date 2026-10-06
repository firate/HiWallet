import { describe, expect, it } from 'vitest'
import { ApiError } from './api'
import { errorText } from './errors'

describe('errorText', () => {
  /** Çalışan kuralın ayrıntısını görüyor: hangi limit, ne kadar. */
  it('iş kuralı reddinde sunucunun mesajını gösteriyor', () => {
    const error = new ApiError(422, {
      detail: 'Tutar personel promo tavanını aşıyor: tavan 500 TRY.',
      rule: 'promo_grant_rejected',
    })

    expect(errorText(error)).toBe('Tutar personel promo tavanını aşıyor: tavan 500 TRY.')
  })

  /** Bazı uçların doğrulama cevabında başlık çerçevenin İngilizce metni; ayrıntı alanlarda. */
  it('alan hatasında başlık yerine kontrol etmeyi söylüyor', () => {
    const error = new ApiError(400, {
      title: 'One or more validation errors occurred.',
      errors: { reason: ['İptalin sebebi zorunlu.'] },
    })

    expect(errorText(error)).toBe('Bilgileri kontrol et.')
  })

  it('beklenmeyen hatada çerçevenin metnini göstermiyor', () => {
    const error = new ApiError(500, { title: 'An error occurred while processing your request.' })

    expect(errorText(error)).toBe('Beklenmeyen bir hata oluştu. Biraz sonra tekrar dene.')
  })
})

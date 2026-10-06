import { ApiError } from './api'

/**
 * Hatanın panelde gösterilen metni. Çalışan iş kuralının ayrıntısını sunucunun
 * mesajından görüyor. İki istisna: alan hatasında başlık bazı uçlarda çerçevenin
 * İngilizce metni ve ayrıntı alanların altında; beklenmeyen hatanın gövdesinde iç
 * ayrıntı yok, yalnızca çerçevenin metni var.
 */
export function errorText(error: Error): string {
  if (!(error instanceof ApiError)) {
    return error.message
  }

  if (error.status === 400 && Object.keys(error.problem?.errors ?? {}).length > 0) {
    return 'Bilgileri kontrol et.'
  }

  if (error.status === 500) {
    return 'Beklenmeyen bir hata oluştu. Biraz sonra tekrar dene.'
  }

  return error.message
}

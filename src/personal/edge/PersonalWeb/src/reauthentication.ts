import { ApiError, reauthenticationUrl } from './api'

/** Sunucu işlemi parolayla yakın zamanda yapılmış bir giriş olmadan yapmıyor. */
export function needsReauthentication(error: Error | null): boolean {
  return error instanceof ApiError && error.problem?.rule === 'reauthentication_required'
}

/**
 * Keycloak'ın giriş sayfasına gider; oturum açık olsa da parola soruluyor. Dönüşte aynı
 * sayfa. Uygulamanın dışına çıkılıyor, sayfa tamamen değişiyor.
 */
export function reauthenticate(returnPath: string): void {
  window.location.assign(reauthenticationUrl(returnPath))
}

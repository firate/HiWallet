import { useState } from 'react'

/**
 * Para hareketi başlatan formun Idempotency-Key'i. Başarılı cevaba kadar AYNI kalıyor:
 * cevabı gelmeyen ya da hata dönen istek yeniden gönderildiğinde sunucu onu aynı işlem
 * sayıyor ve ikinci kez para göndermiyor. Yeni anahtar ancak işlem tamamlanınca.
 */
export function useIdempotencyKey(): [key: string, renew: () => void] {
  const [key, setKey] = useState(() => crypto.randomUUID())

  return [key, () => setKey(crypto.randomUUID())]
}

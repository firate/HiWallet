import { useQuery } from '@tanstack/react-query'
import { api } from './api'
import type { AccountLimits, KycLevel, KycMovement } from './types'

/**
 * Hesabın seviye limitleri. Seviyesi olmayan (işyeri) hesapta sorulmuyor. Okunamazsa
 * düğmeler açık kalıyor: kararı zaten sunucu veriyor, ekran yalnızca önden söylüyor.
 */
export function useLimits(accountId: string | undefined, level: KycLevel | null | undefined, currency: string | undefined) {
  return useQuery({
    queryKey: ['accounts', accountId, 'limits', currency],
    queryFn: () => api.limits(accountId ?? '', currency ?? ''),
    enabled: accountId !== undefined && currency !== undefined && level !== null && level !== undefined,
  })
}

/** Hareket neden yapılamıyor; yapılabiliyorsa `null`. */
export function blockedReason(limits: AccountLimits | undefined, movement: KycMovement): string | null {
  const item = limits?.movements.find((candidate) => candidate.movement === movement)

  if (!item || item.remaining > 0) {
    return null
  }

  return item.limit === 0 ? 'Doğrulama seviyen buna izin vermiyor.' : 'Bu ayki limitin doldu.'
}

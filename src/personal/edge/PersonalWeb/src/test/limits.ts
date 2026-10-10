import type { AccountLimits, KycLevel, KycMovement } from '../types'

/** Sunucunun limit cevabı; verilmeyen hareketler açık ve kullanılmamış. */
export function limitsOf(
  kycLevel: KycLevel,
  overrides: Partial<Record<KycMovement, { limit: number; used?: number }>> = {},
  balanceCap: number | null = null,
): AccountLimits {
  const movements: KycMovement[] = ['IncomingTransfer', 'OutgoingTransfer', 'Payment', 'Withdrawal', 'Deposit', 'IncomingTotal']

  return {
    accountId: 'a1',
    kycLevel,
    currency: 'TRY',
    periodStart: '2026-10-01T00:00:00Z',
    movements: movements.map((movement) => {
      const { limit, used = 0 } = overrides[movement] ?? { limit: 50_000 }
      return { movement, limit, used, remaining: Math.max(0, limit - used) }
    }),
    balanceCap,
    balance: 300,
  }
}

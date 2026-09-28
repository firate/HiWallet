const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
}

export function date(value: string): string {
  return dateFormat.format(new Date(value))
}

const fundTypes: Record<string, string> = {
  cash: 'Nakit',
  card: 'Kart',
  promo: 'Promo',
}

export function fundType(value: string): string {
  return fundTypes[value] ?? value
}

const movementTypes: Record<string, string> = {
  p2p: 'Transfer',
  p2b: 'İşyerine transfer',
  b2p: 'İşyerinden gelen',
  b2b: 'İşyerleri arası',
  payment: 'Ödeme',
  topup: 'Para yükleme',
  withdrawal: 'Para çekme',
  refund: 'İade',
  promo_grant: 'Promo yüklemesi',
  promo_expiry: 'Promo süresi doldu',
}

export function movementType(value: string): string {
  return movementTypes[value] ?? value
}

const withdrawalStates: Record<string, string> = {
  initiated: 'Alındı',
  rejected: 'Reddedildi',
  debited: 'Cüzdandan düşüldü',
  bank_transfer_pending: 'Bankada',
  settling: 'Gönderildi',
  completed: 'Gönderildi',
  compensating: 'İade ediliyor',
  failed: 'Gönderilemedi, iade edildi',
}

export function withdrawalState(value: string): string {
  return withdrawalStates[value] ?? value
}

/** Sonucu artık değişmeyen durumlar; izleme burada duruyor. */
export function isFinalWithdrawalState(value: string): boolean {
  return value === 'rejected' || value === 'completed' || value === 'failed'
}

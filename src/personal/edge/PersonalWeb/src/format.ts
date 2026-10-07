const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
}

export function date(value: string): string {
  return dateFormat.format(new Date(value))
}

/** Hesap numarası okunsun diye üçlü gruplar halinde: 123 456 7890. */
export function accountNumber(value: string): string {
  return `${value.slice(0, 3)} ${value.slice(3, 6)} ${value.slice(6)}`
}

/** IBAN dörtlü gruplar halinde, bankaların yazdığı gibi: TR28 0009 9000 .... */
export function iban(value: string): string {
  return value.replace(/(.{4})/g, '$1 ').trim()
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

const kycLevels: Record<string, string> = {
  Unknown: 'Doğrulanmadı',
  Unverified: 'Temel doğrulama',
  Verified: 'Doğrulanmış',
  Contracted: 'Sözleşmeli',
}

export function kycLevel(value: string): string {
  return kycLevels[value] ?? value
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
  under_review: 'İncelemede',
  cancelling: 'İptal ediliyor',
  cancelled: 'İptal edildi, iade edildi',
}

export function withdrawalState(value: string): string {
  return withdrawalStates[value] ?? value
}

/** Sonucu artık değişmeyen durumlar; izleme burada duruyor. */
export function isFinalWithdrawalState(value: string): boolean {
  return value === 'rejected' || value === 'completed' || value === 'failed' || value === 'cancelled'
}

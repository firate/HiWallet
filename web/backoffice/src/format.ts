import type { StaffRole } from './types'

const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
}

export function date(value: string): string {
  return dateFormat.format(new Date(value))
}

const staffRoles: Record<StaffRole, string> = {
  support: 'Destek',
  operations: 'Operasyon',
  finance: 'Finans',
  marketing: 'Pazarlama',
}

/** Çalışanın rolleri; Keycloak'ın kendi varsayılan rolleri (offline_access gibi) gösterilmiyor. */
export function staffRoleNames(roles: string[]): string[] {
  return roles.filter((role): role is StaffRole => role in staffRoles).map((role) => staffRoles[role])
}

const accountTypes: Record<string, string> = {
  Person: 'Bireysel',
  Business: 'İşyeri',
}

export function accountType(value: string): string {
  return accountTypes[value] ?? value
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

/** Panelde listelenebilen çekim durumları; inceleme kuyruğu ilk sırada. */
export const withdrawalStates: Record<string, string> = {
  under_review: 'İncelemede',
  initiated: 'Alındı',
  debited: 'Cüzdandan düşüldü',
  bank_transfer_pending: 'Bankada',
  settling: 'Gönderiliyor',
  completed: 'Gönderildi',
  compensating: 'İade ediliyor',
  failed: 'Gönderilemedi, iade edildi',
  cancelling: 'İptal ediliyor',
  cancelled: 'İptal edildi, iade edildi',
  rejected: 'Reddedildi',
}

export function withdrawalState(value: string): string {
  return withdrawalStates[value] ?? value
}

const campaignRules: Record<string, string> = {
  payment_to_merchant: 'İşyerine ödeme',
  daily_payment_total: 'Günlük ödeme toplamı',
}

export function campaignRule(value: string): string {
  return campaignRules[value] ?? value
}

const promoScopes: Record<string, string> = {
  all_businesses: 'Bütün işyerleri',
  selected_businesses: 'Seçili işyerleri',
}

export function promoScope(value: string): string {
  return promoScopes[value] ?? value
}

/** Satır başına bir hesap kimliği; boş satırlar atlanıyor. */
export function idList(text: string): string[] {
  return text
    .split(/\s+/)
    .map((id) => id.trim())
    .filter((id) => id.length > 0)
}

import type { StaffPermission } from './types'

const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
}

export function date(value: string): string {
  return dateFormat.format(new Date(value))
}

const permissions: readonly StaffPermission[] = [
  'customer.view',
  'withdrawal.review',
  'promo.grant',
  'campaign.view',
  'campaign.manage',
  'merchant.promo_acceptance',
  'staff.manage',
]

const keycloakRoles = /^(default-roles-.+|offline_access|uma_authorization)$/

/** Çalışanın panelde tanımlanmış rolleri: token'daki izinler ve Keycloak'ın kendi rolleri hariç. */
export function staffRoleNames(roles: string[]): string[] {
  return roles.filter((role) => !permissions.includes(role as StaffPermission) && !keycloakRoles.test(role))
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

const auditActions: Record<string, string> = {
  role_created: 'Rol açıldı',
  role_updated: 'Rol değişti',
  role_deleted: 'Rol silindi',
  staff_invited: 'Çalışan davet edildi',
  staff_roles_changed: 'Rolleri değişti',
  staff_disabled: 'Kapatıldı',
  staff_enabled: 'Açıldı',
  invitation_sent: 'Davet yeniden gönderildi',
}

export function auditAction(value: string): string {
  return auditActions[value] ?? value
}

export function staffStatus(staff: { enabled: boolean; invitationPending: boolean }): string {
  if (!staff.enabled) return 'Kapalı'
  return staff.invitationPending ? 'Davet bekliyor' : 'Etkin'
}

export function personName(staff: { firstName: string | null; lastName: string | null; email: string }): string {
  const name = [staff.firstName, staff.lastName].filter(Boolean).join(' ')
  return name || staff.email
}

/** Satır başına bir hesap kimliği; boş satırlar atlanıyor. */
export function idList(text: string): string[] {
  return text
    .split(/\s+/)
    .map((id) => id.trim())
    .filter((id) => id.length > 0)
}

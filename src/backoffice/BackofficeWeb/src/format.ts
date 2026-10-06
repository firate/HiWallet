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

/** Havalenin askıya alınma sebebi. */
const depositHoldReasons: Record<string, string> = {
  no_account_number: 'Açıklamada hesap numarası yok',
  ambiguous_account_number: 'Açıklamada birden fazla numara var',
  unknown_account: 'Numaranın hesabı yok',
  business_account: 'İşyeri hesabı',
  no_wallet_in_currency: 'Bu para biriminde cüzdan yok',
  unknown_sender: 'Bankanın bildiriminde gönderenin kimliği yok',
  sender_not_holder: 'Gönderen hesabın sahibi değil',
  limit_exceeded: 'Seviye limiti',
}

export function depositHoldReason(value: string): string {
  return depositHoldReasons[value] ?? value
}

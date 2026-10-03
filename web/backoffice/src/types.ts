// BFF'in cevapları. Şekil EdgeApi.Core/Contracts'taki tiplerle aynı; alan adları
// camelCase, tutarlar sayı.

/** Oturumdaki kimlik. Yetki burada yok; o personel yönetiminden (StaffAccess). */
export interface SessionUser {
  subject: string
  name: string | null
  email: string | null
}

/**
 * Çalışanın şu anki rolleri ve izinleri, personel yönetiminden. Token'da değil: rolü
 * alınan çalışanın bir sonraki isteği reddediliyor ve panel bunu buradan yeniden okuyor.
 */
export interface StaffAccess {
  subject: string
  roles: string[]
  permissions: StaffPermission[]
}

/** Çalışanın izinleri; kod yalnızca bunları tanıyor. Roller panelde bunlardan kuruluyor. */
export type StaffPermission =
  | 'customer.view'
  | 'withdrawal.review'
  | 'promo.grant'
  | 'campaign.view'
  | 'campaign.manage'
  | 'merchant.promo_acceptance'
  | 'staff.manage'

export type KycLevel = 'Unknown' | 'Unverified' | 'Verified' | 'Contracted'

export interface Balance {
  fundType: 'cash' | 'card' | 'promo'
  balance: number
}

export interface AccountWallet {
  walletId: string
  name: string
  currency: string
  balance: number
  withdrawable: number
  balances: Balance[]
}

/** Hesabın cüzdanı; varsayılan olan hesap numarasına gelen parayı alıyor. */
export interface AccountDetailWallet extends AccountWallet {
  isDefault: boolean
}

export interface AccountDetail {
  accountId: string
  /** On hane; müşteri ve çalışan hesabı bununla tanıyor. */
  accountNumber: string
  type: 'Person' | 'Business'
  /** Bireysel hesabın doğrulama seviyesi; işyeri hesabında null. */
  kycLevel: KycLevel | null
  /** Platform fonlu promo bu işyerinde geçiyor mu; bireysel hesapta hep false. */
  acceptsPromo: boolean
  createdAt: string
  wallets: AccountDetailWallet[]
}

export interface Wallet extends AccountWallet {
  accountId: string
}

export interface Movement {
  movementId: number
  transactionId: string
  type: string
  amount: number
  currency: string
  fundType: string
  createdAt: string
}

export interface MovementsPage {
  items: Movement[]
  size: number
  nextCursor: number | null
}

export type PromoScope = 'all_businesses' | 'selected_businesses'

export interface Promo {
  grantId: string
  amount: number
  remaining: number
  currency: string
  funder: string
  scope: PromoScope
  merchantAccountIds: string[]
  expiresAt: string | null
  expired: boolean
  createdAt: string
}

export interface PromosPage {
  items: Promo[]
  size: number
  nextCursor: string | null
}

export interface StaffPromoRequest {
  amount: number
  currency: string
  scope: PromoScope
  merchantAccountIds: string[] | null
  expiresAt: string | null
}

export interface PromoGrant {
  grantId: string
  replayed: boolean
}

export interface Withdrawal {
  withdrawalId: string
  accountId: string
  walletId: string
  state: string
  amount: number
  currency: string
  /** Maskeli. */
  destinationIban: string
  totalDebited: number | null
  failureReason: string | null
  createdAt: string
  updatedAt: string
}

export interface WithdrawalsPage {
  items: Withdrawal[]
  size: number
  nextCursor: string | null
}

export type CampaignRule = 'payment_to_merchant' | 'daily_payment_total'

export type RewardType = 'fixed' | 'percentage'

export interface CampaignRequest {
  name: string
  rule: CampaignRule
  thresholdAmount: number | null
  rewardType: RewardType
  rewardAmount: number | null
  rewardRate: number | null
  rewardMax: number | null
  currency: string
  grantScope: PromoScope
  grantValidForDays: number | null
  budget: number
  dailyCapPerAccount: number
  totalCapPerAccount: number
  startsAt: string
  endsAt: string | null
  triggerMerchantAccountIds: string[] | null
  scopeMerchantAccountIds: string[] | null
}

export interface Campaign {
  campaignId: string
  name: string
  rule: CampaignRule
  thresholdAmount: number | null
  rewardType: RewardType
  rewardAmount: number | null
  rewardRate: number | null
  rewardMax: number | null
  currency: string
  grantScope: PromoScope
  grantValidForDays: number | null
  budget: number
  dailyCapPerAccount: number
  totalCapPerAccount: number
  startsAt: string
  endsAt: string | null
  triggerMerchantAccountIds: string[]
  scopeMerchantAccountIds: string[]
  /** Şimdiye kadar verilen partilerin toplamı; bütçeden düşülen. */
  granted: number
  createdAt: string
  createdBy: string | null
  endedBy: string | null
}

export interface CampaignsPage {
  items: Campaign[]
  size: number
  nextCursor: string | null
}

export interface Permission {
  name: StaffPermission
  description: string
}

/** Panelin rolü: izin seti. */
export interface Role {
  roleId: string
  name: string
  description: string | null
  permissions: StaffPermission[]
}

export interface RolesPage {
  items: Role[]
  first: number
  size: number
  nextFirst: number | null
}

export interface StaffSummary {
  staffId: string
  email: string
  firstName: string | null
  lastName: string | null
  enabled: boolean
  /** Çalışan parolasını ya da OTP'sini henüz kurmadı. */
  invitationPending: boolean
  createdAt: string
}

export interface RoleDetail extends Role {
  members: StaffSummary[]
}

export interface StaffPage {
  items: StaffSummary[]
  first: number
  size: number
  nextFirst: number | null
}

export interface StaffDetail extends StaffSummary {
  roles: { roleId: string; name: string }[]
}

export interface RoleRequest {
  description: string | null
  permissions: StaffPermission[]
}

export interface InviteRequest {
  email: string
  firstName: string | null
  lastName: string | null
  roleIds: string[]
}

export interface AuditEvent {
  eventId: string
  occurredAt: string
  actorSubject: string
  actorName: string | null
  action: string
  targetType: 'role' | 'staff'
  targetId: string
  targetLabel: string
  details: Record<string, unknown>
}

export interface AuditEventsPage {
  items: AuditEvent[]
  size: number
  nextCursor: string | null
}

/**
 * RFC 7807. Doğrulama hatalarında alan başına mesajlar `errors`'ta; iş kuralı
 * reddinde makinenin okuyacağı ad `rule`'da.
 */
export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  rule?: string
  errors?: Record<string, string[]>
}

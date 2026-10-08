// BFF'in cevapları. Şekil EdgeApi.Core/Contracts'taki tiplerle aynı; alan adları
// camelCase, tutarlar sayı.

export interface SessionUser {
  subject: string
  name: string | null
  email: string | null
}

/** Bireysel hesabın doğrulama seviyesi; işyeri hesabında null. */
export type KycLevel = 'Unknown' | 'Unverified' | 'Verified' | 'Contracted'

export interface Account {
  accountId: string
  /** On hane; para bu numarayla gönderiliyor. */
  accountNumber: string
  type: 'Person' | 'Business'
  kycLevel: KycLevel | null
  createdAt: string
}

export interface AccountsPage {
  items: Account[]
  size: number
  nextCursor: string | null
}

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

export interface AccountDetail extends Account {
  /** Platform fonlu promo bu işyerinde geçiyor mu; bireysel hesapta hep false. */
  acceptsPromo: boolean
  wallets: AccountDetailWallet[]
}

/** Seviye limitinin sayıldığı hareket. */
export type KycMovement = 'IncomingTransfer' | 'OutgoingTransfer' | 'Payment' | 'Withdrawal' | 'Deposit' | 'IncomingTotal'

/** Hareketin aylık limiti ve bu ay kullanılanı. Limit sıfırsa hareket bu seviyede kapalı. */
export interface MovementLimit {
  movement: KycMovement
  limit: number
  used: number
  remaining: number
}

/**
 * Seviyenin aylık limitleri. Kullanım sunucunun limit kontrolünün saydığıyla aynı; bakiye
 * tavanı yalnızca kimliği tespit edilmemiş seviyede.
 */
export interface AccountLimits {
  accountId: string
  kycLevel: KycLevel
  currency: string
  periodStart: string
  movements: MovementLimit[]
  balanceCap: number | null
  balance: number
}

/**
 * Havaleyle yükleme: bu IBAN'a, alıcı adı bu, açıklamaya bu numara. Havale yalnızca
 * müşterinin kendi adına kayıtlı hesabından kabul ediliyor.
 */
export interface DepositInstructions {
  iban: string
  accountHolder: string
  /** Hesap numarası; açıklamaya yazılacak. */
  reference: string
  currency: string
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

export interface Promo {
  grantId: string
  amount: number
  remaining: number
  currency: string
  funder: string
  scope: 'selected_businesses' | 'all_businesses'
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

export type TransferType = 'P2P' | 'Payment'

/** Alıcı hesap numarasıyla; para alıcının bu para birimindeki varsayılan cüzdanına düşüyor. */
export interface TransferRequest {
  fromWalletId: string
  toAccountNumber: string
  amount: number
  currency: string
  type: TransferType
}

export interface TransferResponse {
  transactionId: string
  replayed: boolean
}

export interface WithdrawalRequest {
  walletId: string
  amount: number
  currency: string
  destinationIban: string
}

export interface WithdrawalAccepted {
  withdrawalId: string
  state: string
  replayed: boolean
}

export interface Withdrawal {
  withdrawalId: string
  accountId: string
  walletId: string
  state: string
  amount: number
  currency: string
  destinationIban: string
  totalDebited: number | null
  failureReason: string | null
  createdAt: string
  updatedAt: string
}

/** Dönüş adresi yok: BFF onu kendi adresinden kuruyor. */
export interface CardTopupRequest {
  walletId: string
  amount: number
  currency: string
}

export interface CardTopupAccepted {
  cardTopupId: string
  walletId: string
  state: string
  amount: number
  currency: string
  paymentUrl: string | null
  expiresAt: string
  failureReason: string | null
  replayed: boolean
}

export interface CardTopup {
  cardTopupId: string
  walletId: string
  /** `created`, `pending`, `paid`, `failed`, `rejected`. */
  state: string
  amount: number
  currency: string
  paymentUrl: string | null
  expiresAt: string
  failureReason: string | null
  createdAt: string
  updatedAt: string
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

export interface RegistrationStarted {
  registrationId: string
  codeExpiresAt: string
}

export interface RegistrationCompleted {
  accountId: string
  email: string
}

/** Temel doğrulamanın adımları ve onaylanacak metinlerin güncel sürümleri. */
export interface OnboardingStatus {
  email: string | null
  phone: string | null
  phoneVerified: boolean
  identityVerified: boolean
  basicVerificationCompleted: boolean
  documents: { termsVersion: string; privacyNoticeVersion: string }
}

export interface PhoneVerificationStarted {
  verificationId: string
  phone: string
  expiresAt: string
}

export interface IdentityRequest {
  firstName: string
  lastName: string
  nationalId: string
  birthDate: string
}

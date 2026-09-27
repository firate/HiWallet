// BFF'in cevapları. Şekil EdgeApi.Core/Contracts'taki tiplerle aynı; alan adları
// camelCase, tutarlar sayı.

export interface SessionUser {
  subject: string
  name: string | null
  email: string | null
  /** Çalışanın rolleri; müşteride boş. */
  roles: string[]
}

export interface Account {
  accountId: string
  type: 'Person' | 'Business'
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

export interface AccountDetail extends Account {
  wallets: AccountWallet[]
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

export interface TransferRequest {
  fromWalletId: string
  toWalletId: string
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
  state: string
  amount: number
  currency: string
  destinationIban: string
  totalDebited: number | null
  failureReason: string | null
  createdAt: string
  updatedAt: string
}

/** RFC 7807. Doğrulama hatalarında alan başına mesajlar `errors`'ta. */
export interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

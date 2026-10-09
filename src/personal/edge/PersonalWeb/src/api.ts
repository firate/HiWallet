import type {
  AccountDetail,
  AccountLimits,
  AccountsPage,
  CardTopup,
  CardTopupAccepted,
  CardTopupRequest,
  CardTopupsPage,
  DepositInstructions,
  IdentityRequest,
  MovementsPage,
  OnboardingStatus,
  PhoneVerificationStarted,
  ProblemDetails,
  PromosPage,
  RegistrationCompleted,
  RegistrationStarted,
  SessionUser,
  TransferRequest,
  TransferResponse,
  Wallet,
  Withdrawal,
  WithdrawalAccepted,
  WithdrawalRequest,
  WithdrawalsPage,
} from './types'

/** BFF'in reddi. Mesaj ProblemDetails'ten; iç servisin cevabı olduğu gibi geliyor. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `İstek başarısız oldu (${status}).`)
    this.status = status
    this.problem = problem
  }
}

/** Oturum yok ya da kapandı. Uygulama giriş sayfasını gösteriyor. */
export class SessionExpiredError extends Error {
  constructor() {
    super('Oturum açık değil.')
  }
}

// BFF her API isteğinde bu başlığı istiyor: cookie'yi tarayıcı kendisi ekliyor, bu
// başlığı ise yalnızca bu uygulama ekleyebiliyor.
const csrfHeader = { 'X-CSRF': '1' }

interface SendOptions {
  body?: unknown
  idempotencyKey?: string
}

async function send<T>(method: string, path: string, options: SendOptions = {}): Promise<T> {
  const headers: Record<string, string> = { ...csrfHeader, Accept: 'application/json' }

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  if (options.idempotencyKey !== undefined) {
    headers['Idempotency-Key'] = options.idempotencyKey
  }

  const response = await fetch(path, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: 'same-origin',
  })

  if (response.status === 401) {
    throw new SessionExpiredError()
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

function page(path: string, after: string | number | null | undefined): string {
  return after === null || after === undefined ? path : `${path}?after=${encodeURIComponent(after)}`
}

export const api = {
  user: () => send<SessionUser>('GET', '/bff/user'),

  // Bir müşterinin birkaç hesabı olur; tavan sayfa yeterli.
  accounts: () => send<AccountsPage>('GET', '/v1/accounts?size=100'),
  account: (accountId: string) => send<AccountDetail>('GET', `/v1/accounts/${accountId}`),
  limits: (accountId: string, currency: string) =>
    send<AccountLimits>('GET', `/v1/accounts/${accountId}/limits?currency=${encodeURIComponent(currency)}`),
  openWallet: (accountId: string, name: string, currency: string) =>
    send<Wallet>('POST', `/v1/accounts/${accountId}/wallets`, { body: { name, currency } }),
  setDefaultWallet: (accountId: string, currency: string, walletId: string) =>
    send<void>('PUT', `/v1/accounts/${accountId}/default-wallets/${currency}`, { body: { walletId } }),
  depositInstructions: (accountId: string) =>
    send<DepositInstructions>('GET', `/v1/accounts/${accountId}/deposit-instructions`),

  wallet: (walletId: string) => send<Wallet>('GET', `/v1/wallets/${walletId}`),
  movements: (walletId: string, after?: number | null) =>
    send<MovementsPage>('GET', page(`/v1/wallets/${walletId}/movements`, after)),
  promos: (walletId: string, after?: string | null) =>
    send<PromosPage>('GET', page(`/v1/wallets/${walletId}/promos`, after)),

  transfer: (request: TransferRequest, idempotencyKey: string) =>
    send<TransferResponse>('POST', '/v1/transfers', { body: request, idempotencyKey }),
  withdraw: (request: WithdrawalRequest, idempotencyKey: string) =>
    send<WithdrawalAccepted>('POST', '/v1/withdrawals', { body: request, idempotencyKey }),
  withdrawal: (withdrawalId: string) => send<Withdrawal>('GET', `/v1/withdrawals/${withdrawalId}`),
  walletWithdrawals: (walletId: string, after?: string | null) =>
    send<WithdrawalsPage>('GET', page(`/v1/wallets/${walletId}/withdrawals`, after)),
  startCardTopup: (request: CardTopupRequest, idempotencyKey: string) =>
    send<CardTopupAccepted>('POST', '/v1/card-topups', { body: request, idempotencyKey }),
  cardTopup: (cardTopupId: string) => send<CardTopup>('GET', `/v1/card-topups/${cardTopupId}`),
  walletCardTopups: (walletId: string, after?: string | null) =>
    send<CardTopupsPage>('GET', page(`/v1/wallets/${walletId}/card-topups`, after)),

  // Kayıt, oturumsuz. Hesabı kayıt açıyor; uygulamada hesap açma yok.
  startRegistration: (email: string) =>
    send<RegistrationStarted>('POST', '/v1/registrations', { body: { email } }),
  verifyRegistrationEmail: (registrationId: string, code: string) =>
    send<{ emailVerified: boolean }>('POST', `/v1/registrations/${registrationId}/email-verification`, {
      body: { code },
    }),
  completeRegistration: (registrationId: string, password: string) =>
    send<RegistrationCompleted>('POST', `/v1/registrations/${registrationId}/completion`, { body: { password } }),

  // Temel doğrulama, oturumla.
  onboardingStatus: () => send<OnboardingStatus>('GET', '/v1/me/onboarding'),
  startPhoneVerification: (phone: string) =>
    send<PhoneVerificationStarted>('POST', '/v1/me/phone-verifications', { body: { phone } }),
  confirmPhone: (verificationId: string, code: string) =>
    send<{ phone: string }>('POST', `/v1/me/phone-verifications/${verificationId}/confirmation`, { body: { code } }),
  verifyIdentity: (identity: IdentityRequest) =>
    send<{ nationalId: string }>('PUT', '/v1/me/identity', { body: identity }),
  completeBasicVerification: (termsVersion: string, privacyNoticeVersion: string) =>
    send<{ accountId: string; kycLevel: string }>('POST', '/v1/me/basic-verification', {
      body: { termsVersion, privacyNoticeVersion },
    }),
}

/**
 * Keycloak'a gidip dönen giriş. Dönüşte aynı sayfa açılıyor. Kaydı yeni biten müşteride
 * e-posta formda dolu geliyor.
 */
export function loginUrl(returnUrl: string, loginHint?: string): string {
  const url = `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`
  return loginHint ? `${url}&loginHint=${encodeURIComponent(loginHint)}` : url
}

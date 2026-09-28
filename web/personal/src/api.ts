import type {
  Account,
  AccountDetail,
  AccountsPage,
  MovementsPage,
  ProblemDetails,
  PromosPage,
  SessionUser,
  TransferRequest,
  TransferResponse,
  Wallet,
  Withdrawal,
  WithdrawalAccepted,
  WithdrawalRequest,
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
  openAccount: () => send<Account>('POST', '/v1/accounts'),
  openWallet: (accountId: string, name: string, currency: string) =>
    send<Wallet>('POST', `/v1/accounts/${accountId}/wallets`, { body: { name, currency } }),

  wallet: (walletId: string) => send<Wallet>('GET', `/v1/wallets/${walletId}`),
  movements: (walletId: string, after?: number | null) =>
    send<MovementsPage>('GET', page(`/v1/wallets/${walletId}/movements`, after)),
  promos: (walletId: string) => send<PromosPage>('GET', `/v1/wallets/${walletId}/promos`),

  transfer: (request: TransferRequest, idempotencyKey: string) =>
    send<TransferResponse>('POST', '/v1/transfers', { body: request, idempotencyKey }),
  withdraw: (request: WithdrawalRequest, idempotencyKey: string) =>
    send<WithdrawalAccepted>('POST', '/v1/withdrawals', { body: request, idempotencyKey }),
  withdrawal: (withdrawalId: string) => send<Withdrawal>('GET', `/v1/withdrawals/${withdrawalId}`),
}

/** Keycloak'a gidip dönen giriş. Dönüşte aynı sayfa açılıyor. */
export function loginUrl(returnUrl: string): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

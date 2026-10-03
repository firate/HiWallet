import type {
  AccountDetail,
  Campaign,
  CampaignRequest,
  CampaignsPage,
  MovementsPage,
  ProblemDetails,
  PromoGrant,
  PromosPage,
  SessionUser,
  StaffPromoRequest,
  Wallet,
  Withdrawal,
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

/** Oturum yok ya da kapandı. Panel giriş sayfasını gösteriyor. */
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

  // 204: gövde yok (ör. işyerinin promo kabulü).
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

function page(path: string, after: string | number | null | undefined): string {
  if (after === null || after === undefined) {
    return path
  }

  return `${path}${path.includes('?') ? '&' : '?'}after=${encodeURIComponent(after)}`
}

export const api = {
  user: () => send<SessionUser>('GET', '/bff/user'),

  account: (accountId: string) => send<AccountDetail>('GET', `/v1/accounts/${accountId}`),
  setAcceptsPromo: (accountId: string, acceptsPromo: boolean) =>
    send<void>('PUT', `/v1/accounts/${accountId}/accepts-promo`, { body: { acceptsPromo } }),

  wallet: (walletId: string) => send<Wallet>('GET', `/v1/wallets/${walletId}`),
  movements: (walletId: string, after?: number | null) =>
    send<MovementsPage>('GET', page(`/v1/wallets/${walletId}/movements`, after)),
  promos: (walletId: string, after?: string | null) =>
    send<PromosPage>('GET', page(`/v1/wallets/${walletId}/promos`, after)),
  grantStaffPromo: (walletId: string, request: StaffPromoRequest, idempotencyKey: string) =>
    send<PromoGrant>('POST', `/v1/wallets/${walletId}/promos`, { body: request, idempotencyKey }),

  withdrawals: (state: string, after?: string | null) =>
    send<WithdrawalsPage>('GET', page(`/v1/withdrawals?state=${encodeURIComponent(state)}`, after)),
  withdrawal: (withdrawalId: string) => send<Withdrawal>('GET', `/v1/withdrawals/${withdrawalId}`),
  releaseWithdrawal: (withdrawalId: string) => send<Withdrawal>('POST', `/v1/withdrawals/${withdrawalId}/release`),
  cancelWithdrawal: (withdrawalId: string, reason: string) =>
    send<Withdrawal>('POST', `/v1/withdrawals/${withdrawalId}/cancel`, { body: { reason } }),

  campaigns: (after?: string | null) => send<CampaignsPage>('GET', page('/v1/promo-campaigns', after)),
  campaign: (campaignId: string) => send<Campaign>('GET', `/v1/promo-campaigns/${campaignId}`),
  createCampaign: (request: CampaignRequest) => send<Campaign>('POST', '/v1/promo-campaigns', { body: request }),
  endCampaign: (campaignId: string) => send<Campaign>('POST', `/v1/promo-campaigns/${campaignId}/end`),
}

/** Keycloak'a gidip dönen giriş. Dönüşte aynı sayfa açılıyor. */
export function loginUrl(returnUrl: string): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

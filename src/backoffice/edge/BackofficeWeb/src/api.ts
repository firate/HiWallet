import type {
  AccountDetail,
  AccountLimits,
  CardTopupsPage,
  AuditEventsPage,
  Campaign,
  CampaignRequest,
  CampaignsPage,
  CustomerProfile,
  CustomerSearch,
  CustomerSearchResult,
  InviteRequest,
  MovementsPage,
  Permission,
  ProblemDetails,
  PromoGrant,
  PromosPage,
  Role,
  RoleDetail,
  RoleRequest,
  RolesPage,
  SessionUser,
  StaffAccess,
  StaffDetail,
  StaffPage,
  StaffPromoRequest,
  SuspendedDepositMoved,
  SuspendedDepositsPage,
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

  // Gövdesiz cevap: 204 (işyerinin promo kabulü, rol silme) ya da gövdesiz 202 (davet).
  const text = await response.text()
  return (text.length === 0 ? undefined : JSON.parse(text)) as T
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
  access: () => send<StaffAccess>('GET', '/v1/me'),

  account: (accountId: string) => send<AccountDetail>('GET', `/v1/accounts/${accountId}`),
  limits: (accountId: string, currency: string) =>
    send<AccountLimits>('GET', `/v1/accounts/${accountId}/limits?currency=${encodeURIComponent(currency)}`),
  accountByNumber: (number: string) =>
    send<AccountDetail>('GET', `/v1/accounts/by-number/${encodeURIComponent(number)}`),
  setAcceptsPromo: (accountId: string, acceptsPromo: boolean) =>
    send<void>('PUT', `/v1/accounts/${accountId}/accepts-promo`, { body: { acceptsPromo } }),

  // Kişisel bilgi onboarding'den. Arama ölçütü gövdede: adres erişim log'larına düşüyor.
  customer: (accountId: string) => send<CustomerProfile>('GET', `/v1/customers/by-account/${accountId}`),
  searchCustomers: (criterion: CustomerSearch) =>
    send<CustomerSearchResult>('POST', '/v1/customer-searches', { body: criterion }),

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
  walletWithdrawals: (walletId: string, after?: string | null) =>
    send<WithdrawalsPage>('GET', page(`/v1/wallets/${walletId}/withdrawals`, after)),
  walletCardTopups: (walletId: string, after?: string | null) =>
    send<CardTopupsPage>('GET', page(`/v1/wallets/${walletId}/card-topups`, after)),
  suspendedDeposits: (after?: string | null) =>
    send<SuspendedDepositsPage>('GET', page('/v1/suspended-deposits', after)),
  moveSuspendedDeposit: (depositId: string, accountNumber: string, idempotencyKey: string) =>
    send<SuspendedDepositMoved>('POST', `/v1/suspended-deposits/${depositId}/move`, {
      body: { accountNumber },
      idempotencyKey,
    }),
  releaseWithdrawal: (withdrawalId: string) => send<Withdrawal>('POST', `/v1/withdrawals/${withdrawalId}/release`),
  cancelWithdrawal: (withdrawalId: string, reason: string) =>
    send<Withdrawal>('POST', `/v1/withdrawals/${withdrawalId}/cancel`, { body: { reason } }),

  permissions: () => send<Permission[]>('GET', '/v1/permissions'),
  roles: (first?: number | null) => send<RolesPage>('GET', first ? `/v1/roles?first=${first}&size=100` : '/v1/roles?size=100'),
  role: (roleId: string) => send<RoleDetail>('GET', `/v1/roles/${roleId}`),
  createRole: (name: string, request: RoleRequest) =>
    send<Role>('POST', '/v1/roles', { body: { name, ...request } }),
  updateRole: (roleId: string, request: RoleRequest) => send<Role>('PUT', `/v1/roles/${roleId}`, { body: request }),
  deleteRole: (roleId: string) => send<void>('DELETE', `/v1/roles/${roleId}`),

  staffList: (first: number | null, search: string) => {
    const query = new URLSearchParams()
    if (first) query.set('first', String(first))
    if (search) query.set('search', search)
    const text = query.toString()
    return send<StaffPage>('GET', text ? `/v1/staff?${text}` : '/v1/staff')
  },
  staff: (staffId: string) => send<StaffDetail>('GET', `/v1/staff/${staffId}`),
  inviteStaff: (request: InviteRequest) => send<StaffDetail>('POST', '/v1/staff', { body: request }),
  setStaffRoles: (staffId: string, roleIds: string[]) =>
    send<StaffDetail>('PUT', `/v1/staff/${staffId}/roles`, { body: { roleIds } }),
  disableStaff: (staffId: string) => send<StaffDetail>('POST', `/v1/staff/${staffId}/disable`),
  enableStaff: (staffId: string) => send<StaffDetail>('POST', `/v1/staff/${staffId}/enable`),
  resendInvitation: (staffId: string) => send<void>('POST', `/v1/staff/${staffId}/invitation`),
  auditEvents: (after?: string | null) => send<AuditEventsPage>('GET', page('/v1/audit-events', after)),

  campaigns: (after?: string | null) => send<CampaignsPage>('GET', page('/v1/promo-campaigns', after)),
  campaign: (campaignId: string) => send<Campaign>('GET', `/v1/promo-campaigns/${campaignId}`),
  createCampaign: (request: CampaignRequest) => send<Campaign>('POST', '/v1/promo-campaigns', { body: request }),
  endCampaign: (campaignId: string) => send<Campaign>('POST', `/v1/promo-campaigns/${campaignId}/end`),
}

/** Keycloak'a gidip dönen giriş. Dönüşte aynı sayfa açılıyor. */
export function loginUrl(returnUrl: string): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

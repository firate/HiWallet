import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { AccountDetail, StaffRole } from '../types'

function staff(...roles: StaffRole[]) {
  return { status: 200, body: { subject: 's1', name: 'Çalışan', email: null, roles } }
}

function account(overrides: Partial<AccountDetail>): AccountDetail {
  return {
    accountId: 'a1',
    type: 'Person',
    kycLevel: 'Unverified',
    acceptsPromo: false,
    createdAt: '2026-09-28T10:00:00Z',
    wallets: [{ walletId: 'w1', name: 'Ana', currency: 'TRY', balance: 250, withdrawable: 250, balances: [] }],
    ...overrides,
  }
}

describe('AccountPage', () => {
  it('bireysel hesabı seviyesi ve cüzdanlarıyla gösteriyor', async () => {
    fakeBff({ 'GET /bff/user': staff('support'), 'GET /v1/accounts/a1': { status: 200, body: account({}) } })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText('Temel doğrulama')).toBeTruthy()
    expect(screen.getByRole('link', { name: /Ana/ }).getAttribute('href')).toBe('/cuzdanlar/w1')
    expect(screen.queryByRole('button', { name: /promo/i })).toBeNull()
  })

  it('pazarlama işyerinin promo kabulünü açıyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': staff('marketing'),
      'GET /v1/accounts/a1': [
        { status: 200, body: account({ type: 'Business', kycLevel: null }) },
        { status: 200, body: account({ type: 'Business', kycLevel: null, acceptsPromo: true }) },
      ],
      'PUT /v1/accounts/a1/accepts-promo': { status: 204 },
    })

    renderAt('/hesaplar/a1', <App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Promo kabulünü aç' }))

    expect(await screen.findByRole('button', { name: 'Promo kabulünü kapat' })).toBeTruthy()
    expect(calls.find((call) => call.method === 'PUT')?.body).toEqual({ acceptsPromo: true })
  })
})

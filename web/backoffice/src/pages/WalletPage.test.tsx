import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { StaffPermission } from '../types'

function staff(...permissions: StaffPermission[]) {
  return { status: 200, body: { subject: 's1', name: 'Çalışan', email: null, roles: permissions } }
}

const wallet = {
  status: 200,
  body: {
    walletId: 'w1',
    accountId: 'a1',
    name: 'Ana',
    currency: 'TRY',
    balance: 250,
    withdrawable: 250,
    balances: [{ fundType: 'cash', balance: 250 }],
  },
}

const empty = { status: 200, body: { items: [], size: 20, nextCursor: null } }

describe('WalletPage', () => {
  it('personel promo’su tekrar edilebilir anahtarla veriliyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': staff('customer.view', 'promo.grant', 'campaign.view', 'campaign.manage', 'merchant.promo_acceptance'),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
      'POST /v1/wallets/w1/promos': { status: 201, body: { grantId: 'g1', replayed: false } },
    })

    renderAt('/cuzdanlar/w1', <App />)
    await userEvent.type(await screen.findByLabelText('Tutar'), '100')
    await userEvent.click(screen.getByRole('button', { name: 'Promo ver' }))

    expect(await screen.findByText(/Promo verildi/)).toBeTruthy()
    const grant = calls.find((call) => call.method === 'POST')
    expect(grant?.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(grant?.body).toEqual({
      amount: 100,
      currency: 'TRY',
      scope: 'all_businesses',
      merchantAccountIds: null,
      expiresAt: null,
    })
  })

  it('promo izni olmayan çalışan promo veremiyor, cüzdanı görüyor', async () => {
    fakeBff({
      'GET /bff/user': staff('customer.view'),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
    })

    renderAt('/cuzdanlar/w1', <App />)

    expect(await screen.findByText('Ana')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Promo ver' })).toBeNull()
  })
})

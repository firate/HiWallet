import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'


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
  it('cüzdanın çekimlerini çekim sayfalarına bağlıyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
      'GET /v1/wallets/w1/withdrawals': {
        status: 200,
        body: {
          items: [
            {
              withdrawalId: 'c1',
              accountId: 'a1',
              walletId: 'w1',
              state: 'under_review',
              amount: 9000,
              currency: 'TRY',
              destinationIban: 'TR33******************1326',
              totalDebited: 9180,
              failureReason: null,
              createdAt: '2026-10-08T10:00:00Z',
              updatedAt: '2026-10-08T10:00:05Z',
            },
          ],
          size: 20,
          nextCursor: null,
        },
      },
    })

    renderAt('/cuzdanlar/w1', <App />)

    const link = await screen.findByRole('link', { name: 'İncelemede' })
    expect(link.getAttribute('href')).toBe('/cekimler/c1')
  })

  it('ödenmeyen kartla yüklemenin sebebini gösteriyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
      'GET /v1/wallets/w1/withdrawals': empty,
      'GET /v1/wallets/w1/card-topups': {
        status: 200,
        body: {
          items: [
            {
              cardTopupId: 'k1',
              walletId: 'w1',
              state: 'failed',
              amount: 250,
              currency: 'TRY',
              paymentUrl: null,
              expiresAt: '2026-10-08T10:15:00Z',
              failureReason: 'expired',
              createdAt: '2026-10-08T10:00:00Z',
              updatedAt: '2026-10-08T10:16:00Z',
            },
          ],
          size: 20,
          nextCursor: null,
        },
      },
    })

    renderAt('/cuzdanlar/w1', <App />)

    const row = (await screen.findByText('Ödenmedi')).closest('tr')!
    expect(row.textContent).toContain('expired')
  })

  it('personel promo’su tekrar edilebilir anahtarla veriliyor', async () => {
    const calls = fakeBff({
      ...staffSession(['customer.view', 'promo.grant', 'campaign.view', 'campaign.manage', 'merchant.promo_acceptance']),
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

  it('seçili işyerleri hesap numarasıyla yazılabiliyor', async () => {
    const shop = '0198a0c4-0000-7000-8000-000000000001'
    const calls = fakeBff({
      ...staffSession(['customer.view', 'promo.grant']),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
      'GET /v1/accounts/by-number/1234567897': { status: 200, body: { accountId: shop } },
      'POST /v1/wallets/w1/promos': { status: 201, body: { grantId: 'g1', replayed: false } },
    })

    renderAt('/cuzdanlar/w1', <App />)
    await userEvent.type(await screen.findByLabelText('Tutar'), '100')
    await userEvent.selectOptions(screen.getByLabelText('Kapsam'), 'selected_businesses')
    await userEvent.type(screen.getByLabelText(/İşyerleri/), '123 456 7897')
    await userEvent.click(screen.getByRole('button', { name: 'Promo ver' }))

    expect(await screen.findByText(/Promo verildi/)).toBeTruthy()
    expect(calls.find((call) => call.method === 'POST')?.body).toMatchObject({
      scope: 'selected_businesses',
      merchantAccountIds: [shop],
    })
  })

  it('promo izni olmayan çalışan promo veremiyor, cüzdanı görüyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/wallets/w1': wallet,
      'GET /v1/wallets/w1/movements': empty,
      'GET /v1/wallets/w1/promos': empty,
    })

    renderAt('/cuzdanlar/w1', <App />)

    expect(await screen.findByText('Ana')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Promo ver' })).toBeNull()
  })
})

import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { KycLevel, Promo } from '../types'
import { WalletPage } from './WalletPage'

const wallet = {
  walletId: 'w1',
  accountId: 'a1',
  name: 'Ana',
  currency: 'TRY',
  balance: 100,
  withdrawable: 100,
  balances: [],
}

function account(kycLevel: KycLevel, isDefault: boolean) {
  return {
    accountId: 'a1',
    accountNumber: '1234567897',
    type: 'Person',
    kycLevel,
    createdAt: '2026-10-01T10:00:00Z',
    acceptsPromo: false,
    wallets: [{ ...wallet, isDefault }],
  }
}

function promo(grantId: string, remaining: number): Promo {
  return {
    grantId,
    amount: 50,
    remaining,
    currency: 'TRY',
    funder: 'platform',
    scope: 'all_businesses',
    merchantAccountIds: [],
    expiresAt: null,
    expired: false,
    createdAt: '2026-10-01T10:00:00Z',
  }
}

const noMovements = { status: 200, body: { items: [], size: 50, nextCursor: null } }
const noPromos = { status: 200, body: { items: [], size: 50, nextCursor: null } }

function renderWallet() {
  renderAt(
    '/cuzdanlar/w1',
    <Routes>
      <Route path="cuzdanlar/:walletId" element={<WalletPage />} />
    </Routes>,
  )
}

describe('WalletPage', () => {
  /** Havale hesabın bu para birimindeki varsayılan cüzdanına düşüyor. */
  it('varsayılan cüzdanda havaleyle yüklemeyi gösteriyor', async () => {
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified', true) },
      'GET /v1/wallets/w1/movements': noMovements,
      'GET /v1/wallets/w1/promos': noPromos,
    })

    renderWallet()

    const link = await screen.findByRole('link', { name: 'Havaleyle para yükle' })
    expect(link.getAttribute('href')).toBe('/hesaplar/a1/yukle')
  })

  it('havalenin düşmeyeceği cüzdanda göstermiyor', async () => {
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified', false) },
      'GET /v1/wallets/w1/movements': noMovements,
      'GET /v1/wallets/w1/promos': noPromos,
    })

    renderWallet()

    await screen.findByText('Henüz hareket yok.')
    expect(screen.queryByRole('link', { name: 'Havaleyle para yükle' })).toBeNull()
  })

  it('promo partilerini sayfa sayfa gösteriyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified', true) },
      'GET /v1/wallets/w1/movements': noMovements,
      'GET /v1/wallets/w1/promos': { status: 200, body: { items: [promo('g2', 20)], size: 1, nextCursor: 'g2' } },
      'GET /v1/wallets/w1/promos?after=g2': { status: 200, body: { items: [promo('g1', 10)], size: 1, nextCursor: null } },
    })

    renderWallet()
    await user.click(await screen.findByRole('button', { name: 'Daha eski partiler' }))

    expect(await screen.findByText(/10,00.*kaldı/)).toBeTruthy()
    expect(screen.getByText(/20,00.*kaldı/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Daha eski partiler' })).toBeNull()
  })

  it('promo partileri okunamazsa söylüyor', async () => {
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified', true) },
      'GET /v1/wallets/w1/movements': noMovements,
      'GET /v1/wallets/w1/promos': { status: 404, body: { title: 'Cüzdan bulunamadı' } },
    })

    renderWallet()

    expect(await screen.findByText('Cüzdan bulunamadı')).toBeTruthy()
  })
})

import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { AccountDetailWallet } from '../types'
import { AccountsPage } from './AccountsPage'

const account = {
  accountId: 'a1',
  accountNumber: '1234567897',
  type: 'Person',
  kycLevel: 'Verified',
  createdAt: '2026-10-01T10:00:00Z',
}

function wallet(walletId: string, name: string, isDefault: boolean): AccountDetailWallet {
  return { walletId, name, currency: 'TRY', balance: 0, withdrawable: 0, balances: [], isDefault }
}

describe('AccountsPage', () => {
  it('hesap numarasını okunur gruplarla gösteriyor', async () => {
    fakeBff({
      'GET /v1/accounts?size=100': { status: 200, body: { items: [account], size: 100, nextCursor: null } },
      'GET /v1/accounts/a1': { status: 200, body: { ...account, acceptsPromo: false, wallets: [wallet('w1', 'Ana', true)] } },
    })

    renderAt('/', <AccountsPage />)

    expect(await screen.findByText('123 456 7897')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Havaleyle para yükle' }).getAttribute('href')).toBe('/hesaplar/a1/yukle')
    // Tek cüzdanda seçilecek bir şey yok.
    expect(screen.queryByRole('button', { name: 'Gelen para buraya gelsin' })).toBeNull()
  })

  /** Aynı para biriminde iki cüzdan: gelen paranın hangisine düşeceğini müşteri seçiyor. */
  it('gelen paranın düşeceği cüzdanı seçtiriyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/accounts?size=100': { status: 200, body: { items: [account], size: 100, nextCursor: null } },
      'GET /v1/accounts/a1': [
        { status: 200, body: { ...account, acceptsPromo: false, wallets: [wallet('w1', 'Ana', true), wallet('w2', 'Birikim', false)] } },
        { status: 200, body: { ...account, acceptsPromo: false, wallets: [wallet('w1', 'Ana', false), wallet('w2', 'Birikim', true)] } },
      ],
      'PUT /v1/accounts/a1/default-wallets/TRY': { status: 204 },
    })

    renderAt('/', <AccountsPage />)
    await user.click(await screen.findByRole('button', { name: 'Gelen para buraya gelsin' }))

    expect(calls.find((call) => call.method === 'PUT')?.body).toEqual({ walletId: 'w2' })
    await screen.findByRole('button', { name: 'Gelen para buraya gelsin' })
    expect(screen.getAllByText('Gelen para buraya')).toHaveLength(1)
  })
})

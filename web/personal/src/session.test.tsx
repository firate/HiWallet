import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from './App'
import { fakeBff } from './test/fakeBff'
import { renderAt } from './test/render'

describe('SessionGate', () => {
  it('oturum yoksa girişi, dönülecek sayfayla gösteriyor', async () => {
    fakeBff({ 'GET /bff/user': { status: 401 } })

    renderAt('/cuzdanlar/w1', <App />)

    const login = await screen.findByRole('link', { name: 'Giriş yap' })
    expect(login.getAttribute('href')).toBe('/bff/login?returnUrl=%2Fcuzdanlar%2Fw1')
    expect(screen.getByRole('link', { name: 'Kayıt ol' }).getAttribute('href')).toBe('/kayit')
  })

  it('oturum varsa kullanıcıyı ve cüzdanları gösteriyor', async () => {
    fakeBff({
      'GET /bff/user': { status: 200, body: { subject: 's1', name: 'Ayşe Yılmaz', email: null } },
      'GET /v1/accounts?size=100': {
        status: 200,
        body: { items: [{ accountId: 'a1', accountNumber: '1234567897', type: 'Person', kycLevel: 'Unverified', createdAt: '2026-09-28T10:00:00Z' }], size: 100, nextCursor: null },
      },
      'GET /v1/accounts/a1': {
        status: 200,
        body: {
          accountId: 'a1',
          accountNumber: '1234567897',
          type: 'Person',
          kycLevel: 'Unverified',
          createdAt: '2026-09-28T10:00:00Z',
          wallets: [{ walletId: 'w1', name: 'Ana', currency: 'TRY', balance: 0, withdrawable: 0, balances: [], isDefault: true }],
        },
      },
    })

    renderAt('/', <App />)

    expect(await screen.findByText('Ayşe Yılmaz')).toBeTruthy()
    expect(await screen.findByText('Ana')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Hesap aç' })).toBeNull()
  })

  /** Kayıttan sonra hesap doğrulanmadı: müşteri doğrulamaya yönleniyor. */
  it('doğrulanmamış hesapta doğrulamayı öneriyor', async () => {
    fakeBff({
      'GET /bff/user': { status: 200, body: { subject: 's1', name: null, email: 'ayse@ornek.com' } },
      'GET /v1/accounts?size=100': {
        status: 200,
        body: { items: [{ accountId: 'a1', accountNumber: '1234567897', type: 'Person', kycLevel: 'Unknown', createdAt: '2026-09-28T10:00:00Z' }], size: 100, nextCursor: null },
      },
      'GET /v1/accounts/a1': {
        status: 200,
        body: { accountId: 'a1', accountNumber: '1234567897', type: 'Person', kycLevel: 'Unknown', createdAt: '2026-09-28T10:00:00Z', wallets: [] },
      },
    })

    renderAt('/', <App />)

    const verify = await screen.findByRole('link', { name: 'Doğrulamayı tamamla' })
    expect(verify.getAttribute('href')).toBe('/dogrulama')
  })
})

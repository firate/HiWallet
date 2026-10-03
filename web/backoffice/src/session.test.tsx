import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from './App'
import { fakeBff, staffSession } from './test/fakeBff'
import { renderAt } from './test/render'

describe('SessionGate', () => {
  it('oturum yoksa girişi, dönülecek sayfayla gösteriyor', async () => {
    fakeBff({ 'GET /bff/user': { status: 401 } })

    renderAt('/cekimler', <App />)

    const login = await screen.findByRole('link', { name: 'Giriş yap' })
    expect(login.getAttribute('href')).toBe('/bff/login?returnUrl=%2Fcekimler')
  })

  /** Rolü olmayan ya da kapatılmış çalışanın izni yok; panel ona yalnızca bunu söylüyor. */
  it('izni olmayan çalışana rolü olmadığını söylüyor ve çıkışı bırakıyor', async () => {
    fakeBff(staffSession([]))

    renderAt('/', <App />)

    expect(await screen.findByText(/rol atanmamış/)).toBeTruthy()
    const logout = screen.getByRole('button', { name: 'Çıkış' })
    expect(logout.closest('form')?.getAttribute('action')).toBe('/bff/logout')
  })

  it('çalışanı rolleriyle gösteriyor', async () => {
    fakeBff(staffSession(['customer.view', 'withdrawal.review'], { name: 'Fırat Ergül', roles: ['Finans', 'Operasyon'] }))

    renderAt('/', <App />)

    expect(await screen.findByText('Fırat Ergül')).toBeTruthy()
    expect(screen.getByText('Finans, Operasyon')).toBeTruthy()
  })

  /** Rolü az önce alınan çalışanın isteği 403 alıyor; panel izinleri yeniden okuyup kapanıyor. */
  it('bir istek 403 alınca izinleri yeniden okuyor', async () => {
    const session = staffSession(['customer.view'])
    const calls = fakeBff({
      ...session,
      'GET /v1/me': [session['GET /v1/me'], { status: 200, body: { subject: 's1', roles: [], permissions: [] } }],
      'GET /v1/accounts/a1': { status: 403 },
    })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText(/rol atanmamış/)).toBeTruthy()
    expect(calls.filter((call) => call.path === '/v1/me')).toHaveLength(2)
  })
})

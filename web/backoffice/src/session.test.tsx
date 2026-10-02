import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from './App'
import { fakeBff } from './test/fakeBff'
import { renderAt } from './test/render'

describe('SessionGate', () => {
  it('oturum yoksa girişi, dönülecek sayfayla gösteriyor', async () => {
    fakeBff({ 'GET /bff/user': { status: 401 } })

    renderAt('/cekimler', <App />)

    const login = await screen.findByRole('link', { name: 'Giriş yap' })
    expect(login.getAttribute('href')).toBe('/bff/login?returnUrl=%2Fcekimler')
  })

  /** BFF rolü olmayan çalışanın kullanıcısını da vermiyor; panel bunu 403'ten anlıyor. */
  it('rolü olmayan çalışana rolü olmadığını söylüyor ve çıkışı bırakıyor', async () => {
    fakeBff({ 'GET /bff/user': { status: 403 } })

    renderAt('/', <App />)

    expect(await screen.findByText(/rol atanmamış/)).toBeTruthy()
    const logout = screen.getByRole('button', { name: 'Çıkış' })
    expect(logout.closest('form')?.getAttribute('action')).toBe('/bff/logout')
  })

  it('çalışanı rolleriyle gösteriyor, Keycloak’ın kendi rollerini göstermiyor', async () => {
    fakeBff({
      'GET /bff/user': {
        status: 200,
        body: {
          subject: 's1',
          name: 'Fırat Ergül',
          email: 'calisan@ornek.com',
          roles: ['default-roles-hiwallet-staff', 'operations', 'offline_access', 'finance'],
        },
      },
    })

    renderAt('/', <App />)

    expect(await screen.findByText('Fırat Ergül')).toBeTruthy()
    expect(screen.getByText('Operasyon, Finans')).toBeTruthy()
    expect(screen.queryByText(/offline_access/)).toBeNull()
  })
})

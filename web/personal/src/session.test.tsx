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
  })

  it('oturum varsa kullanıcıyı ve cüzdanları gösteriyor', async () => {
    fakeBff({
      'GET /bff/user': { status: 200, body: { subject: 's1', name: 'Ayşe Yılmaz', email: null } },
      'GET /v1/accounts?size=100': { status: 200, body: { items: [], size: 100, nextCursor: null } },
    })

    renderAt('/', <App />)

    expect(await screen.findByText('Ayşe Yılmaz')).toBeTruthy()
    expect(await screen.findByRole('button', { name: 'Hesap aç' })).toBeTruthy()
  })
})

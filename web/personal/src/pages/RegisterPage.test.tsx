import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'

const email = 'ayse@ornek.com'

async function reachPassword(user: ReturnType<typeof userEvent.setup>) {
  renderAt('/kayit', <App />)

  await user.type(await screen.findByLabelText('E-posta'), email)
  await user.click(screen.getByRole('button', { name: 'Kod gönder' }))
  await user.type(await screen.findByLabelText('Doğrulama kodu'), '123456')
  await user.click(screen.getByRole('button', { name: 'Doğrula' }))
  await screen.findByLabelText('Parola')
}

describe('RegisterPage', () => {
  /** Kayıt oturumsuz; bitince müşteri girişe e-postası dolu gidiyor. */
  it('e-posta, kod ve parolayla kaydı tamamlayıp girişe gönderiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'POST /v1/registrations': { status: 202, body: { registrationId: 'r1', codeExpiresAt: '2026-09-28T10:10:00Z' } },
      'POST /v1/registrations/r1/email-verification': { status: 200, body: { emailVerified: true } },
      'POST /v1/registrations/r1/completion': { status: 200, body: { accountId: 'a1', email } },
    })

    await reachPassword(user)
    await user.type(screen.getByLabelText('Parola'), 'Guclu-Parola-1')
    await user.type(screen.getByLabelText('Parola (tekrar)'), 'Guclu-Parola-1')
    await user.click(screen.getByRole('button', { name: 'Kaydı tamamla' }))

    const login = await screen.findByRole('link', { name: 'Giriş yap' })
    expect(login.getAttribute('href')).toBe('/bff/login?returnUrl=%2F&loginHint=ayse%40ornek.com')
    expect(calls.map((call) => call.body)).toEqual([{ email }, { code: '123456' }, { password: 'Guclu-Parola-1' }])
    expect(calls.every((call) => call.headers['X-CSRF'] === '1')).toBe(true)
  })

  it('parolalar eşleşmezse göndermiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'POST /v1/registrations': { status: 202, body: { registrationId: 'r1', codeExpiresAt: '2026-09-28T10:10:00Z' } },
      'POST /v1/registrations/r1/email-verification': { status: 200, body: { emailVerified: true } },
    })

    await reachPassword(user)
    await user.type(screen.getByLabelText('Parola'), 'Guclu-Parola-1')
    await user.type(screen.getByLabelText('Parola (tekrar)'), 'Baska-Parola-2')
    await user.click(screen.getByRole('button', { name: 'Kaydı tamamla' }))

    expect(await screen.findByText('Parolalar aynı değil.')).toBeTruthy()
    expect(calls.some((call) => call.path.endsWith('/completion'))).toBe(false)
  })

  /** Adresin sahibi olduğu kanıtlandıktan sonra söyleniyor; müşteri girişe yönleniyor. */
  it('kayıtlı e-postada girişi gösteriyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'POST /v1/registrations': { status: 202, body: { registrationId: 'r1', codeExpiresAt: '2026-09-28T10:10:00Z' } },
      'POST /v1/registrations/r1/email-verification': { status: 200, body: { emailVerified: true } },
      'POST /v1/registrations/r1/completion': {
        status: 409,
        body: { title: 'Bu e-postayla bir hesap var. Giriş yap ya da parolanı sıfırla.', rule: 'email_registered' },
      },
    })

    await reachPassword(user)
    await user.type(screen.getByLabelText('Parola'), 'Guclu-Parola-1')
    await user.type(screen.getByLabelText('Parola (tekrar)'), 'Guclu-Parola-1')
    await user.click(screen.getByRole('button', { name: 'Kaydı tamamla' }))

    expect(await screen.findByText('Bu e-postayla bir hesap var. Giriş yap ya da parolanı sıfırla.')).toBeTruthy()
    const login = screen.getByRole('link', { name: 'Giriş yap' })
    expect(login.getAttribute('href')).toBe('/bff/login?returnUrl=%2F&loginHint=ayse%40ornek.com')
  })
})

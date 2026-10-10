import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reauthenticate } from '../reauthentication'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import { ProfilePage } from './ProfilePage'

vi.mock('../reauthentication', async (original) => ({
  ...(await original<typeof import('../reauthentication')>()),
  reauthenticate: vi.fn(),
}))

const status = {
  email: 'musteri@ornek.com',
  phone: '+90 532 *** ** 67',
  phoneVerified: true,
  identityVerified: true,
  basicVerificationCompleted: true,
  documents: { termsVersion: '2026-09', privacyNoticeVersion: '2026-09' },
}

const reauthenticationRequired = {
  status: 403,
  body: { title: 'Numaranı değiştirmek için parolanla yeniden giriş yap.', rule: 'reauthentication_required' },
}

function renderProfile(path = '/profil') {
  renderAt(
    path,
    <Routes>
      <Route path="profil" element={<ProfilePage />} />
    </Routes>,
  )
}

describe('ProfilePage', () => {
  beforeEach(() => {
    vi.mocked(reauthenticate).mockClear()
    sessionStorage.clear()
  })

  it('kodu yeni numaraya gönderip numarayı değiştiriyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/me/onboarding': { status: 200, body: status },
      'POST /v1/me/phone-changes': {
        status: 202,
        body: { verificationId: 'v1', phone: '+90 533 *** ** 11', expiresAt: '2026-10-09T10:10:00Z' },
      },
      'POST /v1/me/phone-changes/v1/confirmation': {
        status: 200,
        body: { phone: '+90 533 *** ** 11', withdrawalHoldUntil: '2026-10-10T10:00:00Z' },
      },
    })
    renderProfile()

    await user.type(await screen.findByLabelText('Yeni numara'), '05331234511')
    await user.click(screen.getByRole('button', { name: 'Kod gönder' }))
    await user.type(await screen.findByLabelText('Kod'), '123456')
    await user.click(screen.getByRole('button', { name: 'Numarayı değiştir' }))

    const done = await screen.findByRole('status')
    expect(done.textContent).toContain('Numaran değişti: +90 533 *** ** 11')
    expect(done.textContent).toContain('banka hesabına para çekemezsin')
    expect(calls.find((call) => call.path === '/v1/me/phone-changes')?.body).toEqual({ phone: '05331234511' })
  })

  /** Açık oturum yetmiyor: parolayla yeniden giriş, dönüşte numara dolu. */
  it('yeniden giriş istenirse girişe gidiyor, numarayı saklıyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'GET /v1/me/onboarding': { status: 200, body: status },
      'POST /v1/me/phone-changes': reauthenticationRequired,
    })
    renderProfile()

    await user.type(await screen.findByLabelText('Yeni numara'), '05331234511')
    await user.click(screen.getByRole('button', { name: 'Kod gönder' }))

    await vi.waitFor(() => expect(reauthenticate).toHaveBeenCalledWith('/profil?yeniden=1'))
    expect(sessionStorage.getItem('hiwallet.telefon-degisikligi')).toBe('05331234511')
  })

  /** Dönüşten sonra hâlâ isteniyorsa ikinci kez yönlendirmiyor: döngü yok, sebep görünüyor. */
  it('girişten döndükten sonra yeniden yönlendirmiyor', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('hiwallet.telefon-degisikligi', '05331234511')
    fakeBff({
      'GET /v1/me/onboarding': { status: 200, body: status },
      'POST /v1/me/phone-changes': reauthenticationRequired,
    })
    renderProfile('/profil?yeniden=1')

    expect(((await screen.findByLabelText('Yeni numara')) as HTMLInputElement).value).toBe('05331234511')
    await user.click(screen.getByRole('button', { name: 'Kod gönder' }))

    expect(await screen.findByText('Numaranı değiştirmek için parolanla yeniden giriş yapman gerekiyor.')).toBeTruthy()
    expect(reauthenticate).not.toHaveBeenCalled()
  })

  it('temel doğrulamayı bitirmeyene doğrulamayı gösteriyor', async () => {
    fakeBff({ 'GET /v1/me/onboarding': { status: 200, body: { ...status, basicVerificationCompleted: false } } })
    renderProfile()

    expect((await screen.findByRole('link', { name: 'Doğrulamayı tamamla' })).getAttribute('href')).toBe('/dogrulama')
    expect(screen.queryByLabelText('Yeni numara')).toBeNull()
  })
})

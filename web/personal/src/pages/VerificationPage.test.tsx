import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import { VerificationPage } from './VerificationPage'

function status(overrides: Record<string, unknown> = {}) {
  return {
    email: 'ayse@ornek.com',
    phone: null,
    phoneVerified: false,
    identityVerified: false,
    basicVerificationCompleted: false,
    documents: { termsVersion: '2026-09', privacyNoticeVersion: '2026-09' },
    ...overrides,
  }
}

describe('VerificationPage', () => {
  /** Adımlar sırayla: telefon, kimlik, onaylar. Her adımdan sonra durum yeniden soruluyor. */
  it('telefon, kimlik ve onaylarla temel doğrulamayı tamamlıyor', async () => {
    const user = userEvent.setup()
    const phone = '+90 532 *** ** 67'
    const calls = fakeBff({
      'GET /v1/me/onboarding': [
        { status: 200, body: status() },
        { status: 200, body: status({ phoneVerified: true, phone }) },
        { status: 200, body: status({ phoneVerified: true, phone, identityVerified: true }) },
        { status: 200, body: status({ phoneVerified: true, phone, identityVerified: true, basicVerificationCompleted: true }) },
      ],
      'POST /v1/me/phone-verifications': {
        status: 202,
        body: { verificationId: 'v1', phone, expiresAt: '2026-09-28T10:10:00Z' },
      },
      'POST /v1/me/phone-verifications/v1/confirmation': { status: 200, body: { phone } },
      'PUT /v1/me/identity': { status: 200, body: { nationalId: '10*******46' } },
      'POST /v1/me/basic-verification': { status: 200, body: { accountId: 'a1', kycLevel: 'Unverified' } },
    })

    renderAt(
      '/dogrulama',
      <Routes>
        <Route path="dogrulama" element={<VerificationPage />} />
      </Routes>,
    )

    await user.type(await screen.findByLabelText('Cep telefonu'), '05321234567')
    await user.click(screen.getByRole('button', { name: 'Kod gönder' }))
    await user.type(await screen.findByLabelText('SMS kodu'), '654321')
    await user.click(screen.getByRole('button', { name: 'Doğrula' }))

    await user.type(await screen.findByLabelText('Ad'), 'Ayşe')
    await user.type(screen.getByLabelText('Soyad'), 'Yılmaz')
    await user.type(screen.getByLabelText('T.C. kimlik numarası'), '10000000146')
    await user.type(screen.getByLabelText('Doğum tarihi'), '1990-05-17')
    await user.click(screen.getByRole('button', { name: 'Kimliği doğrula' }))

    await user.click(await screen.findByLabelText(/Kullanıcı sözleşmesini/))
    await user.click(screen.getByLabelText(/aydınlatma metnini/))
    await user.click(screen.getByRole('button', { name: 'Onayla' }))

    expect(await screen.findByText('Temel doğrulama tamamlandı.')).toBeTruthy()

    const bodies = Object.fromEntries(
      calls.filter((call) => call.method !== 'GET').map((call) => [`${call.method} ${call.path}`, call.body]),
    )
    expect(bodies['POST /v1/me/phone-verifications']).toEqual({ phone: '05321234567' })
    expect(bodies['POST /v1/me/phone-verifications/v1/confirmation']).toEqual({ code: '654321' })
    expect(bodies['PUT /v1/me/identity']).toEqual({
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      nationalId: '10000000146',
      birthDate: '1990-05-17',
    })
    expect(bodies['POST /v1/me/basic-verification']).toEqual({ termsVersion: '2026-09', privacyNoticeVersion: '2026-09' })
  })

  it('onaylar işaretlenmeden göndermiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/me/onboarding': { status: 200, body: status({ phoneVerified: true, identityVerified: true }) },
    })

    renderAt(
      '/dogrulama',
      <Routes>
        <Route path="dogrulama" element={<VerificationPage />} />
      </Routes>,
    )

    await user.click(await screen.findByRole('button', { name: 'Onayla' }))

    expect(calls.some((call) => call.path === '/v1/me/basic-verification')).toBe(false)
  })
})

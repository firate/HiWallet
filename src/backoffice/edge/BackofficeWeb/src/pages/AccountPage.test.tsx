import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { AccountDetail } from '../types'


function account(overrides: Partial<AccountDetail>): AccountDetail {
  return {
    accountId: 'a1',
    accountNumber: '1234567897',
    type: 'Person',
    kycLevel: 'Unverified',
    acceptsPromo: false,
    createdAt: '2026-09-28T10:00:00Z',
    wallets: [{ walletId: 'w1', name: 'Ana', currency: 'TRY', balance: 250, withdrawable: 250, balances: [], isDefault: true }],
    ...overrides,
  }
}

describe('AccountPage', () => {
  it('bireysel hesabı seviyesi ve cüzdanlarıyla gösteriyor', async () => {
    fakeBff({ ...staffSession(['customer.view']), 'GET /v1/accounts/a1': { status: 200, body: account({}) } })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText('Temel doğrulama')).toBeTruthy()
    expect(screen.getByText('123 456 7897')).toBeTruthy()
    expect(screen.getByRole('link', { name: /Ana/ }).getAttribute('href')).toBe('/cuzdanlar/w1')
    expect(screen.queryByRole('button', { name: /promo/i })).toBeNull()
  })

  it('bireysel hesabın seviye limitlerini ve bu ay kullanılanı gösteriyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/a1': { status: 200, body: account({}) },
      'GET /v1/accounts/a1/limits?currency=TRY': {
        status: 200,
        body: {
          accountId: 'a1',
          kycLevel: 'Unverified',
          currency: 'TRY',
          periodStart: '2026-10-01T00:00:00Z',
          movements: [
            { movement: 'OutgoingTransfer', limit: 0, used: 0, remaining: 0 },
            { movement: 'Payment', limit: 5500, used: 1200, remaining: 4300 },
          ],
          balanceCap: 5500,
          balance: 250,
        },
      },
    })

    renderAt('/hesaplar/a1', <App />)

    const outgoing = (await screen.findByText('Başka birine gönderim')).closest('tr')!
    expect(outgoing.textContent).toContain('Kapalı')
    expect(screen.getByText('İşyerine ödeme').closest('tr')!.textContent).toMatch(/4\.300,00/)
    expect(screen.getByText(/Bakiye tavanı/).textContent).toMatch(/5\.500,00/)
  })

  it('izinli çalışan işyerinin promo kabulünü açıyor', async () => {
    const calls = fakeBff({
      ...staffSession(['customer.view', 'promo.grant', 'campaign.view', 'campaign.manage', 'merchant.promo_acceptance']),
      'GET /v1/accounts/a1': [
        { status: 200, body: account({ type: 'Business', kycLevel: null }) },
        { status: 200, body: account({ type: 'Business', kycLevel: null, acceptsPromo: true }) },
      ],
      'PUT /v1/accounts/a1/accepts-promo': { status: 204 },
    })

    renderAt('/hesaplar/a1', <App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Promo kabulünü aç' }))

    expect(await screen.findByRole('button', { name: 'Promo kabulünü kapat' })).toBeTruthy()
    expect(calls.find((call) => call.method === 'PUT')?.body).toEqual({ acceptsPromo: true })
    expect(calls.some((call) => call.path.startsWith('/v1/customers/'))).toBe(false)
  })

  /** Kimlik numarası ve telefon onboarding'den maskeli geliyor; panel açık halini hiç görmüyor. */
  it('bireysel hesabın sahibini, onaylarını ve numara değişikliklerini gösteriyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/a1': { status: 200, body: account({}) },
      'GET /v1/customers/by-account/a1': {
        status: 200,
        body: {
          accountId: 'a1',
          email: 'ayse@ornek.com',
          firstName: 'Ayşe',
          lastName: 'Yılmaz',
          nationalId: '10*******46',
          birthDate: '1990-05-17',
          phone: '+90 532 *** ** 45',
          phoneVerifiedAt: '2026-10-02T09:00:00Z',
          identityVerifiedAt: '2026-09-28T10:05:00Z',
          basicVerifiedAt: '2026-09-28T10:06:00Z',
          consents: [
            { document: 'Terms', version: '2026-09', acceptedAt: '2026-09-28T10:06:00Z' },
            { document: 'PrivacyNotice', version: '2026-09', acceptedAt: '2026-09-28T10:06:00Z' },
          ],
          phoneChanges: [{ oldPhone: '+90 555 *** ** 12', newPhone: '+90 532 *** ** 45', changedAt: '2026-10-02T09:00:00Z' }],
        },
      },
    })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText('Ayşe Yılmaz')).toBeTruthy()
    expect(screen.getByText('ayse@ornek.com')).toBeTruthy()
    expect(screen.getByText('10*******46')).toBeTruthy()
    expect(screen.getByText('17.05.1990')).toBeTruthy()
    expect(screen.getByText('Kullanıcı sözleşmesi').closest('tr')!.textContent).toContain('2026-09')
    expect(screen.getByText('KVKK aydınlatma metni')).toBeTruthy()
    const change = screen.getByText('+90 555 *** ** 12').closest('tr')!
    expect(change.textContent).toContain('+90 532 *** ** 45')
  })

  it('doğrulamaya başlamamış müşteride yalnızca e-postayı gösteriyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/a1': { status: 200, body: account({ kycLevel: 'Unknown' }) },
      'GET /v1/customers/by-account/a1': {
        status: 200,
        body: {
          accountId: 'a1',
          email: 'ayse@ornek.com',
          firstName: null,
          lastName: null,
          nationalId: null,
          birthDate: null,
          phone: null,
          phoneVerifiedAt: null,
          identityVerifiedAt: null,
          basicVerifiedAt: null,
          consents: [],
          phoneChanges: [],
        },
      },
    })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText('ayse@ornek.com')).toBeTruthy()
    expect(screen.getByText('Kimlik doğrulanmadı.')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Onaylar' })).toBeNull()
  })

  /** Kayıttan açılmamış hesap (test ya da taşınmış veri): onboarding 404 veriyor. */
  it('hesabın kaydı yoksa kişisel bilgisi olmadığını söylüyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/a1': { status: 200, body: account({}) },
      'GET /v1/customers/by-account/a1': { status: 404, body: { title: 'Hesabın kaydı yok.' } },
    })

    renderAt('/hesaplar/a1', <App />)

    expect(await screen.findByText('Hesabın kayıt bilgisi yok; kişisel bilgisi görüntülenemiyor.')).toBeTruthy()
  })
})

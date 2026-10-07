import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useSearchParams } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { openPaymentPage } from '../paymentPage'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { KycLevel } from '../types'
import { CardTopupPage } from './CardTopupPage'

vi.mock('../paymentPage', () => ({ openPaymentPage: vi.fn() }))

const wallet = {
  walletId: 'w1',
  accountId: 'a1',
  name: 'Ana',
  currency: 'TRY',
  balance: 100,
  withdrawable: 100,
  balances: [],
}

function account(kycLevel: KycLevel) {
  return {
    accountId: 'a1',
    accountNumber: '1234567897',
    type: 'Person',
    kycLevel,
    createdAt: '2026-10-01T10:00:00Z',
    acceptsPromo: false,
    wallets: [{ ...wallet, isDefault: true }],
  }
}

function accepted(state: string, replayed: boolean) {
  return {
    cardTopupId: 'c1',
    walletId: 'w1',
    state,
    amount: 250,
    currency: 'TRY',
    paymentUrl: 'https://odeme.example/odeme/p1',
    expiresAt: '2026-10-06T10:15:00Z',
    failureReason: null,
    replayed,
  }
}

/** Sonuç sayfasının yerinde: hangi yüklemeye gidildiğini gösteriyor. */
function Result() {
  const [params] = useSearchParams()
  return <p>Sonuç: {params.get('cardTopupId')}</p>
}

function renderCardTopup() {
  renderAt(
    '/cuzdanlar/w1/kartla-yukle',
    <Routes>
      <Route path="cuzdanlar/:walletId/kartla-yukle" element={<CardTopupPage />} />
      <Route path="kart-yukleme" element={<Result />} />
    </Routes>,
  )
}

async function start(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Tutar (TRY)'), '250')
  await user.click(screen.getByRole('button', { name: 'Ödemeye geç' }))
}

describe('CardTopupPage', () => {
  beforeEach(() => {
    vi.mocked(openPaymentPage).mockClear()
  })

  it('ödeme açılınca müşteriyi ödeme sayfasına gönderiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified') },
      'POST /v1/card-topups': { status: 202, body: accepted('pending', false) },
    })
    renderCardTopup()

    await start(user)

    await vi.waitFor(() => expect(openPaymentPage).toHaveBeenCalledWith('https://odeme.example/odeme/p1'))
    const post = calls.find((call) => call.method === 'POST')
    expect(post?.body).toEqual({ walletId: 'w1', amount: 250, currency: 'TRY' })
    expect(post?.headers['Idempotency-Key']).toBeTruthy()
  })

  /** Limit ödemeden ÖNCE kontrol ediliyor: yetmiyorsa kart hiç sorulmuyor. */
  it('limit yetmiyorsa ödeme sayfasına göndermiyor, sebebini söylüyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified') },
      'POST /v1/card-topups': {
        status: 422,
        body: { detail: "'Kyc.IncomingTotal.Monthly' limiti 5500 TRY.", rule: 'card_topup_limit' },
      },
    })
    renderCardTopup()

    await start(user)

    expect(await screen.findByText('Bu yükleme doğrulama seviyenin limitine sığmıyor.')).toBeTruthy()
    expect(openPaymentPage).not.toHaveBeenCalled()
  })

  /** Aynı anahtarla tekrar: sonucu belli olan yükleme için ikinci kez ödeme istenmiyor. */
  it('sonucu belli olan tekrar edilmiş yüklemede sonucunu gösteriyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unverified') },
      'POST /v1/card-topups': { status: 202, body: accepted('paid', true) },
    })
    renderCardTopup()

    await start(user)

    expect(await screen.findByText('Sonuç: c1')).toBeTruthy()
    expect(openPaymentPage).not.toHaveBeenCalled()
  })

  /** Doğrulanmamış hesabın gelen para limiti sıfır. */
  it('doğrulanmamış hesapta önce doğrulamayı istiyor', async () => {
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: account('Unknown') },
    })
    renderCardTopup()

    const link = await screen.findByRole('link', { name: 'Doğrulamayı tamamla' })
    expect(link.getAttribute('href')).toBe('/dogrulama')
    expect(screen.queryByRole('button', { name: 'Ödemeye geç' })).toBeNull()
  })
})

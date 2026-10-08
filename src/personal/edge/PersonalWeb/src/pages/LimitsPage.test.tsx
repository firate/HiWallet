import { screen, within } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { limitsOf } from '../test/limits'
import { renderAt } from '../test/render'
import { LimitsPage } from './LimitsPage'

const account = {
  accountId: 'a1',
  accountNumber: '1234567897',
  type: 'Person',
  kycLevel: 'Unverified',
  createdAt: '2026-10-01T10:00:00Z',
  acceptsPromo: false,
  wallets: [{ walletId: 'w1', name: 'Ana', currency: 'TRY', balance: 300, withdrawable: 300, balances: [], isDefault: true }],
}

function renderLimits() {
  renderAt(
    '/hesaplar/a1/limitler',
    <Routes>
      <Route path="hesaplar/:accountId/limitler" element={<LimitsPage />} />
    </Routes>,
  )
}

describe('LimitsPage', () => {
  it('kapalı hareketi, kullanılanı ve bakiye tavanını gösteriyor', async () => {
    const calls = fakeBff({
      'GET /v1/accounts/a1': { status: 200, body: account },
      'GET /v1/accounts/a1/limits?currency=TRY': {
        status: 200,
        body: limitsOf(
          'Unverified',
          { OutgoingTransfer: { limit: 0 }, Withdrawal: { limit: 0 }, Payment: { limit: 5_500, used: 1_200 } },
          5_500,
        ),
      },
    })

    renderLimits()

    const outgoing = (await screen.findByText('Başka birine gönderim')).closest('tr')!
    expect(within(outgoing).getByText('Kapalı')).toBeTruthy()

    const payment = screen.getByText('İşyerine ödeme').closest('tr')!
    expect(within(payment).getByText(/1\.200,00/)).toBeTruthy()
    expect(within(payment).getByText(/4\.300,00/)).toBeTruthy()

    expect(screen.getByText(/Bakiye tavanı/).textContent).toMatch(/5\.500,00/)
    expect(calls.some((call) => call.path === '/v1/accounts/a1/limits?currency=TRY')).toBe(true)
  })

  it('işyeri hesabında limit sormuyor', async () => {
    const calls = fakeBff({ 'GET /v1/accounts/a1': { status: 200, body: { ...account, type: 'Business', kycLevel: null } } })

    renderLimits()

    expect(await screen.findByText('İşyeri hesabının seviye limiti yok.')).toBeTruthy()
    expect(calls.some((call) => call.path.includes('/limits'))).toBe(false)
  })
})

import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import { WithdrawalStatusPage } from './WithdrawalStatusPage'

function withdrawal(state: string, failureReason: string | null, failureRule: string | null) {
  return {
    withdrawalId: 'c1',
    accountId: 'a1',
    walletId: 'w1',
    state,
    amount: 100,
    currency: 'TRY',
    destinationIban: 'TR33******************1326',
    totalDebited: null,
    failureReason,
    failureRule,
    createdAt: '2026-10-08T10:00:00Z',
    updatedAt: '2026-10-08T10:00:05Z',
  }
}

function renderStatus() {
  renderAt(
    '/cekimler/c1',
    <Routes>
      <Route path="cekimler/:withdrawalId" element={<WithdrawalStatusPage />} />
    </Routes>,
  )
}

describe('WithdrawalStatusPage', () => {
  /** Sunucunun sebep metni hesap kimliği ve iç kural adı taşıyor; müşteriye gösterilmiyor. */
  it('reddin sebebini müşterinin diliyle söylüyor', async () => {
    fakeBff({
      'GET /v1/withdrawals/c1': {
        status: 200,
        body: withdrawal(
          'rejected',
          "Hesap 3f2a... için 'Kyc.Withdrawal.Monthly' limiti aşıldı: limit 0 TRY, denenen 102 TRY.",
          'Kyc.Withdrawal.Monthly',
        ),
      },
    })

    renderStatus()

    expect(await screen.findByText('Doğrulama seviyenin aylık çekim limiti bu tutara izin vermedi.')).toBeTruthy()
    expect(screen.queryByText(/3f2a/)).toBeNull()
  })

  it('kuralı bilinmeyen sebepte iç metni göstermiyor', async () => {
    fakeBff({ 'GET /v1/withdrawals/c1': { status: 200, body: withdrawal('rejected', 'Cüzdan yok: 9b1c...', null) } })

    renderStatus()

    expect(await screen.findByText('Çekim yapılamadı.')).toBeTruthy()
    expect(screen.queryByText(/9b1c/)).toBeNull()
  })
})

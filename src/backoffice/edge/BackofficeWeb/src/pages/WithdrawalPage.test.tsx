import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { Withdrawal } from '../types'


function withdrawal(state: string): Withdrawal {
  return {
    withdrawalId: 'c1',
    accountId: 'a1',
    walletId: 'w1',
    state,
    amount: 15000,
    currency: 'TRY',
    destinationIban: 'TR33 **** **** **** **** 1326',
    totalDebited: 15005,
    failureReason: null,
    createdAt: '2026-10-01T10:00:00Z',
    updatedAt: '2026-10-01T10:00:05Z',
  }
}

describe('WithdrawalPage', () => {
  it('inceleme izniyle çekim serbest bırakılıyor', async () => {
    const calls = fakeBff({
      ...staffSession(['customer.view', 'withdrawal.review']),
      'GET /v1/withdrawals/c1': { status: 200, body: withdrawal('under_review') },
      'POST /v1/withdrawals/c1/release': { status: 200, body: withdrawal('bank_transfer_pending') },
    })

    renderAt('/cekimler/c1', <App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Serbest bırak' }))

    expect(await screen.findByText('Bankada')).toBeTruthy()
    const release = calls.find((call) => call.method === 'POST')
    expect(release?.path).toBe('/v1/withdrawals/c1/release')
    expect(release?.headers['X-CSRF']).toBe('1')
  })

  it('inceleme izniyle iptal sebebiyle gönderiliyor', async () => {
    const calls = fakeBff({
      ...staffSession(['customer.view', 'withdrawal.review']),
      'GET /v1/withdrawals/c1': { status: 200, body: withdrawal('under_review') },
      'POST /v1/withdrawals/c1/cancel': { status: 202, body: withdrawal('cancelling') },
    })

    renderAt('/cekimler/c1', <App />)
    await userEvent.type(await screen.findByLabelText('İptal sebebi'), 'Müşteri talebi')
    await userEvent.click(screen.getByRole('button', { name: 'İptal et' }))

    expect(await screen.findByText('İptal ediliyor')).toBeTruthy()
    expect(calls.find((call) => call.path === '/v1/withdrawals/c1/cancel')?.body).toEqual({ reason: 'Müşteri talebi' })
  })

  it('çekim inceleme izni olmayan çalışan kararı göremiyor', async () => {
    fakeBff({
      ...staffSession(['customer.view', 'campaign.view']),
      'GET /v1/withdrawals/c1': { status: 200, body: withdrawal('under_review') },
    })

    renderAt('/cekimler/c1', <App />)

    expect(await screen.findByText('İncelemede')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Serbest bırak' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'İptal et' })).toBeNull()
  })

  it('incelemede olmayan çekimde karar yok', async () => {
    fakeBff({
      ...staffSession(['customer.view', 'withdrawal.review']),
      'GET /v1/withdrawals/c1': { status: 200, body: withdrawal('completed') },
    })

    renderAt('/cekimler/c1', <App />)

    expect(await screen.findByText('Gönderildi')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Serbest bırak' })).toBeNull()
  })
})

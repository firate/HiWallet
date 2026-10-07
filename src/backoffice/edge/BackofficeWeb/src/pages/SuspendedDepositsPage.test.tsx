import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { SuspendedDeposit } from '../types'

function suspended(id: string, reason: string, accountId: string | null, accountNumber: string | null): SuspendedDeposit {
  return {
    id,
    provider: 'bank-fake',
    bankReference: `GLN${id}`,
    amount: 120,
    currency: 'TRY',
    reason,
    accountId,
    accountNumber,
    receivedAt: '2026-10-04T09:00:00Z',
    createdAt: '2026-10-04T09:00:02Z',
  }
}

describe('SuspendedDepositsPage', () => {
  /** Sebep okunur metinle; açıklamadaki numaranın hesabı varsa ona bağlantı. */
  it('askıdaki havaleleri sebebi ve hesabıyla listeliyor', async () => {
    fakeBff({
      ...staffSession(['customer.view', 'deposit.view']),
      'GET /v1/suspended-deposits': {
        status: 200,
        body: {
          items: [
            suspended('1', 'sender_not_holder', 'a1', '1234567897'),
            suspended('2', 'no_account_number', null, null),
          ],
          size: 20,
          nextCursor: null,
        },
      },
    })

    renderAt('/havaleler', <App />)

    expect(await screen.findByText('Gönderen hesabın sahibi değil')).toBeTruthy()
    expect(screen.getByText('Açıklamada hesap numarası yok')).toBeTruthy()
    expect(screen.getByRole('link', { name: '123 456 7897' }).getAttribute('href')).toBe('/hesaplar/a1')
    expect(screen.getByRole('link', { name: 'Askıdaki havaleler' })).toBeTruthy()
  })

  /** İzni olmayan çalışanın menüsünde yok. */
  it('izni olmayanın menüsünde görünmüyor', async () => {
    fakeBff({
      ...staffSession(['customer.view']),
    })

    renderAt('/', <App />)

    expect(await screen.findByRole('link', { name: 'Çekimler' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Askıdaki havaleler' })).toBeNull()
  })
})

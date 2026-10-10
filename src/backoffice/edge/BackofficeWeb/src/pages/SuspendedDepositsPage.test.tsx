import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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

  /**
   * Çalışan parayı açıklamadaki ya da kendi bulduğu hesaba aktarıyor. Anahtar istekle
   * gidiyor; aktarılan havale listeden çıkıyor.
   */
  it('izinli çalışan havaleyi hesabın cüzdanına aktarıyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      ...staffSession(['customer.view', 'deposit.view', 'deposit.resolve']),
      'GET /v1/suspended-deposits': [
        { status: 200, body: { items: [suspended('1', 'sender_not_holder', 'a1', '1234567897')], size: 20, nextCursor: null } },
        { status: 200, body: { items: [], size: 20, nextCursor: null } },
      ],
      'POST /v1/suspended-deposits/1/move': {
        status: 200,
        body: { suspendedDepositId: '1', accountId: 'a2', walletId: 'w2', ledgerTransactionId: 't1', replayed: false },
      },
    })

    renderAt('/havaleler', <App />)
    await user.click(await screen.findByRole('button', { name: 'Cüzdana aktar' }))
    const number = screen.getByLabelText('Hesap numarası') as HTMLInputElement
    expect(number.value).toBe('1234567897')
    await user.clear(number)
    await user.type(number, '987 654 3210')
    await user.click(screen.getByRole('button', { name: 'Aktar' }))

    expect(await screen.findByText('Havale 987 654 3210 hesabının cüzdanına aktarıldı.')).toBeTruthy()
    expect(await screen.findByText('Askıda havale yok.')).toBeTruthy()
    const move = calls.find((call) => call.method === 'POST')!
    expect(move.body).toEqual({ accountNumber: '9876543210' })
    expect(move.headers['Idempotency-Key']).toBeTruthy()
  })

  it('limit yetmezse wallet-api’nin mesajını gösteriyor, havale listede kalıyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      ...staffSession(['customer.view', 'deposit.view', 'deposit.resolve']),
      'GET /v1/suspended-deposits': {
        status: 200,
        body: { items: [suspended('1', 'limit_exceeded', 'a1', '1234567897')], size: 20, nextCursor: null },
      },
      'POST /v1/suspended-deposits/1/move': {
        status: 422,
        body: { title: 'İşlem iş kuralı gereği reddedildi', detail: 'Alıcı bu tutarı alamıyor.', rule: 'Kyc.Deposit.Monthly' },
      },
    })

    renderAt('/havaleler', <App />)
    await user.click(await screen.findByRole('button', { name: 'Cüzdana aktar' }))
    await user.click(screen.getByRole('button', { name: 'Aktar' }))

    expect(await screen.findByText('Alıcı bu tutarı alamıyor.')).toBeTruthy()
    expect(screen.getByText('Seviye limiti')).toBeTruthy()
  })

  it('aktarma izni olmayan yalnızca listeyi görüyor', async () => {
    fakeBff({
      ...staffSession(['customer.view', 'deposit.view']),
      'GET /v1/suspended-deposits': {
        status: 200,
        body: { items: [suspended('1', 'sender_not_holder', 'a1', '1234567897')], size: 20, nextCursor: null },
      },
    })

    renderAt('/havaleler', <App />)

    expect(await screen.findByText('Gönderen hesabın sahibi değil')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Cüzdana aktar' })).toBeNull()
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

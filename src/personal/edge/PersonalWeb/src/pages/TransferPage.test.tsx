import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { limitsOf } from '../test/limits'
import { renderAt } from '../test/render'
import { TransferPage } from './TransferPage'

const wallet = {
  walletId: 'w1',
  accountId: 'a1',
  name: 'Ana',
  currency: 'TRY',
  balance: 100,
  withdrawable: 100,
  balances: [],
}

function renderTransfer() {
  renderAt(
    '/cuzdanlar/w1/transfer',
    <Routes>
      <Route path="cuzdanlar/:walletId/transfer" element={<TransferPage />} />
    </Routes>,
  )
}

async function send(user: ReturnType<typeof userEvent.setup>, amount: string) {
  await user.type(screen.getByLabelText('Alıcının hesap numarası'), '123 456 7897')
  await user.type(screen.getByLabelText('Tutar (TRY)'), amount)
  await user.click(screen.getByRole('button', { name: 'Gönder' }))
}

describe('TransferPage', () => {
  /**
   * Cevabı hata olan istek aynı anahtarla yeniden gönderiliyor: sunucu ilk isteği
   * işlediyse ikincisi yeni bir transfer yapmıyor. Yeni anahtar ancak başarıdan sonra.
   */
  it('hatadan sonra aynı anahtarla, başarıdan sonra yeni anahtarla gönderiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'POST /v1/transfers': [
        { status: 503, body: { title: 'Servis şu an yanıt vermiyor.' } },
        { status: 201, body: { transactionId: 't1', replayed: false } },
      ],
    })
    renderTransfer()
    await screen.findByText('Para gönder')

    await send(user, '25')
    await screen.findByText('Servis şu an yanıt vermiyor.')
    await user.click(screen.getByRole('button', { name: 'Gönder' }))
    await screen.findByText('Gönderildi.')

    await send(user, '10')
    await screen.findAllByText('Gönderildi.')

    expect(calls.find((call) => call.method === 'POST')?.body).toMatchObject({
      fromWalletId: 'w1',
      toAccountNumber: '123 456 7897',
      amount: 25,
      currency: 'TRY',
    })

    const keys = calls.filter((call) => call.method === 'POST').map((call) => call.headers['Idempotency-Key'])
    expect(keys).toHaveLength(3)
    expect(keys[1]).toBe(keys[0])
    expect(keys[2]).not.toBe(keys[0])
  })

  /** Temel doğrulamada başka birine gönderim kapalı, işyerine ödeme açık. */
  it('kişiye gönderim kapalıysa işyerine ödemeyle açılıyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: { accountId: 'a1', type: 'Person', kycLevel: 'Unverified', wallets: [] } },
      'GET /v1/accounts/a1/limits?currency=TRY': {
        status: 200,
        body: limitsOf('Unverified', { OutgoingTransfer: { limit: 0 } }, 5_500),
      },
      'POST /v1/transfers': { status: 201, body: { transactionId: 't1', replayed: false } },
    })
    renderTransfer()

    const person = await screen.findByRole('option', { name: 'Bir kişiye (kapalı)' })
    expect((person as HTMLOptionElement).disabled).toBe(true)
    expect((screen.getByLabelText('Ne için') as HTMLSelectElement).value).toBe('Payment')

    await send(user, '25')
    await screen.findByText('Gönderildi.')

    expect(calls.find((call) => call.method === 'POST')?.body).toMatchObject({ type: 'Payment' })
  })

  it('iki gönderim türü de kapalıysa formu göstermiyor', async () => {
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'GET /v1/accounts/a1': { status: 200, body: { accountId: 'a1', type: 'Person', kycLevel: 'Unknown', wallets: [] } },
      'GET /v1/accounts/a1/limits?currency=TRY': {
        status: 200,
        body: limitsOf('Unknown', { OutgoingTransfer: { limit: 0 }, Payment: { limit: 0 } }, 0),
      },
    })
    renderTransfer()

    expect(await screen.findByText(/Şu an para gönderemiyorsun/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Gönder' })).toBeNull()
  })

  it('tekrar edilmiş gönderimi söylüyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      'GET /v1/wallets/w1': { status: 200, body: wallet },
      'POST /v1/transfers': { status: 201, body: { transactionId: 't1', replayed: true } },
    })
    renderTransfer()
    await screen.findByText('Para gönder')

    await send(user, '25')

    expect(await screen.findByRole('status')).toHaveProperty(
      'textContent',
      'Bu gönderim daha önce yapılmıştı; tekrar gönderilmedi.',
    )
  })
})

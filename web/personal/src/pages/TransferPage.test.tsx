import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
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
  await user.type(screen.getByLabelText('Alıcının cüzdan numarası'), 'w2')
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

    const keys = calls.filter((call) => call.method === 'POST').map((call) => call.headers['Idempotency-Key'])
    expect(keys).toHaveLength(3)
    expect(keys[1]).toBe(keys[0])
    expect(keys[2]).not.toBe(keys[0])
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

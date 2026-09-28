import { describe, expect, it } from 'vitest'
import { api, ApiError, SessionExpiredError } from './api'
import { fakeBff } from './test/fakeBff'

describe('api', () => {
  it('her isteğe CSRF başlığını ekliyor', async () => {
    const calls = fakeBff({ 'GET /v1/wallets/w1': { status: 200, body: {} } })

    await api.wallet('w1')

    expect(calls[0].headers['X-CSRF']).toBe('1')
    expect(calls[0].headers['Idempotency-Key']).toBeUndefined()
  })

  it('para hareketinde anahtarı ve gövdeyi gönderiyor', async () => {
    const calls = fakeBff({ 'POST /v1/transfers': { status: 201, body: { transactionId: 't1', replayed: false } } })
    const request = { fromWalletId: 'w1', toWalletId: 'w2', amount: 10, currency: 'TRY', type: 'P2P' as const }

    await api.transfer(request, 'anahtar-1')

    expect(calls[0].headers['Idempotency-Key']).toBe('anahtar-1')
    expect(calls[0].headers['Content-Type']).toBe('application/json')
    expect(calls[0].body).toEqual(request)
  })

  it('401 oturumun bittiğini söylüyor', async () => {
    fakeBff({ 'GET /v1/accounts?size=100': { status: 401 } })

    await expect(api.accounts()).rejects.toBeInstanceOf(SessionExpiredError)
  })

  it('reddin mesajı ProblemDetails’ten', async () => {
    fakeBff({
      'POST /v1/transfers': { status: 422, body: { title: 'Yetersiz bakiye', detail: 'Bakiye 5,00 TRY.' } },
    })

    const error = await api
      .transfer({ fromWalletId: 'w1', toWalletId: 'w2', amount: 10, currency: 'TRY', type: 'P2P' }, 'k')
      .catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(422)
    expect((error as ApiError).message).toBe('Bakiye 5,00 TRY.')
  })
})

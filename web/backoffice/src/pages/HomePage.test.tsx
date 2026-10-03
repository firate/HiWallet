import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'

const account = {
  accountId: '0198a0c4-0000-7000-8000-000000000001',
  accountNumber: '1234567897',
  type: 'Person',
  kycLevel: 'Verified',
  acceptsPromo: false,
  createdAt: '2026-09-28T10:00:00Z',
  wallets: [],
}

describe('HomePage', () => {
  /** Çalışan müşteriyi hesap numarasıyla buluyor; kimliği yapıştırmıyor. */
  it('hesabı numarasıyla buluyor, gruplama boşluklarını atıyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/by-number/1234567897': { status: 200, body: account },
      [`GET /v1/accounts/${account.accountId}`]: { status: 200, body: account },
    })

    renderAt('/', <App />)
    await user.type(await screen.findByLabelText('Kimlik'), '123 456 7897')
    await user.click(screen.getByRole('button', { name: 'Aç' }))

    expect(await screen.findByText('Bireysel hesap')).toBeTruthy()
    expect(calls.some((call) => call.path === '/v1/accounts/by-number/1234567897')).toBe(true)
  })

  it('olmayan numarada wallet-api’nin mesajını gösteriyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      ...staffSession(['customer.view']),
      'GET /v1/accounts/by-number/1234567897': { status: 404, body: { title: 'Hesap bulunamadı' } },
    })

    renderAt('/', <App />)
    await user.type(await screen.findByLabelText('Kimlik'), '1234567897')
    await user.click(screen.getByRole('button', { name: 'Aç' }))

    expect(await screen.findByText('Hesap bulunamadı')).toBeTruthy()
  })

  it('cüzdana yine kimlikle gidiliyor', async () => {
    const user = userEvent.setup()
    fakeBff(staffSession(['customer.view']))

    renderAt('/', <App />)
    await user.selectOptions(await screen.findByLabelText('Kayıt türü'), 'cuzdanlar')
    await user.type(screen.getByLabelText('Kimlik'), '1234567897')
    await user.click(screen.getByRole('button', { name: 'Aç' }))

    expect(await screen.findByText('Kimlik bir GUID olmalı.')).toBeTruthy()
  })
})

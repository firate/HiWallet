import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import { DepositPage } from './DepositPage'

const account = {
  accountId: 'a1',
  accountNumber: '1234567897',
  type: 'Person',
  acceptsPromo: false,
  wallets: [],
  createdAt: '2026-10-01T10:00:00Z',
}

const instructions = {
  iban: 'TR280009900000000000123456',
  accountHolder: 'Hive Elektronik Para A.Ş.',
  reference: '1234567897',
  currency: 'TRY',
}

function renderDeposit() {
  renderAt(
    '/hesaplar/a1/yukle',
    <Routes>
      <Route path="hesaplar/:accountId/yukle" element={<DepositPage />} />
    </Routes>,
  )
}

describe('DepositPage', () => {
  /** Alıcı, IBAN ve açıklamaya yazılacak numara; ikisi de okunur gruplarla. */
  it('IBANı, alıcıyı ve açıklamaya yazılacak numarayı gösteriyor', async () => {
    fakeBff({
      'GET /v1/accounts/a1': { status: 200, body: { ...account, kycLevel: 'Unverified' } },
      'GET /v1/accounts/a1/deposit-instructions': { status: 200, body: instructions },
    })

    renderDeposit()

    expect(await screen.findByText('TR28 0009 9000 0000 0000 1234 56')).toBeTruthy()
    expect(screen.getByText('Hive Elektronik Para A.Ş.')).toBeTruthy()
    expect(screen.getByText('123 456 7897')).toBeTruthy()
    expect(screen.getByText(/kendi adına kayıtlı/)).toBeTruthy()
  })

  /** Doğrulanmamış hesaba gelen havale cüzdana geçmiyor: önce doğrulama. */
  it('doğrulanmamış hesapta önce doğrulamaya yönlendiriyor', async () => {
    fakeBff({
      'GET /v1/accounts/a1': { status: 200, body: { ...account, kycLevel: 'Unknown' } },
      'GET /v1/accounts/a1/deposit-instructions': { status: 200, body: instructions },
    })

    renderDeposit()

    expect(await screen.findByRole('link', { name: 'Doğrulamayı tamamla' })).toBeTruthy()
    expect(screen.queryByText('TR28 0009 9000 0000 0000 1234 56')).toBeNull()
  })
})

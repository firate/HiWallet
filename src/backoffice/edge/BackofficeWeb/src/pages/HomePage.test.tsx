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

  /** Yetkiyi iç servis kontrol ediyor; menü çalışanı reddedilecek sayfaya götürmüyor. */
  it('menüde ve ana sayfada yalnızca izni olan işleri gösteriyor', async () => {
    fakeBff(staffSession(['staff.manage']))

    renderAt('/', <App />)

    expect(await screen.findByRole('link', { name: 'Personel' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Çekimler' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Kampanyalar' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Kayıt aç' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Müşteri ara' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'İş kuyrukları' })).toBeNull()
  })

  it('askıdaki havaleler iş kuyruklarında, kayıt türleri izne göre', async () => {
    fakeBff(staffSession(['customer.view', 'deposit.view']))

    renderAt('/', <App />)

    const queue = await screen.findByRole('heading', { name: 'İş kuyrukları' })
    expect(queue.parentElement?.querySelector('a[href="/havaleler"]')?.textContent).toBe('Askıdaki havaleler')
    expect(screen.queryByRole('link', { name: 'Promo kampanyaları' })).toBeNull()

    const types = Array.from((screen.getByLabelText('Kayıt türü') as HTMLSelectElement).options).map(
      (option) => option.value,
    )
    expect(types).toEqual(['hesaplar', 'cuzdanlar', 'cekimler'])
  })

  /** Kimlik numarası adreste değil gövdede gidiyor: adres erişim log'larına düşüyor. */
  it('müşteriyi kimlik numarasıyla arıyor, sonuçtan hesaba gidiyor', async () => {
    const user = userEvent.setup()
    const calls = fakeBff({
      ...staffSession(['customer.view']),
      'POST /v1/customer-searches': {
        status: 200,
        body: {
          items: [
            {
              accountId: account.accountId,
              email: 'ayse@ornek.com',
              firstName: 'Ayşe',
              lastName: 'Yılmaz',
              phone: '+90 532 *** ** 45',
            },
          ],
        },
      },
      [`GET /v1/accounts/${account.accountId}`]: { status: 200, body: account },
    })

    renderAt('/', <App />)
    await user.selectOptions(await screen.findByLabelText('Arama ölçütü'), 'nationalId')
    await user.type(screen.getByLabelText('Aranan'), ' 10000000146 ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    const match = await screen.findByRole('link', { name: /Ayşe Yılmaz/ })
    expect(match.textContent).toContain('+90 532 *** ** 45')
    const search = calls.find((call) => call.path === '/v1/customer-searches')!
    expect(search.body).toEqual({ nationalId: '10000000146' })

    await user.click(match)
    expect(await screen.findByText('Bireysel hesap')).toBeTruthy()
  })

  it('eşleşme yoksa söylüyor, geçersiz ölçütte onboarding’in mesajını gösteriyor', async () => {
    const user = userEvent.setup()
    fakeBff({
      ...staffSession(['customer.view']),
      'POST /v1/customer-searches': [
        { status: 200, body: { items: [] } },
        {
          status: 400,
          body: { title: 'One or more validation errors occurred.', errors: { Phone: ['Türkiye’de bir cep telefonu numarası gir.'] } },
        },
      ],
    })

    renderAt('/', <App />)
    await user.type(await screen.findByLabelText('Aranan'), 'yok@ornek.com')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    expect(await screen.findByText('Eşleşen müşteri yok.')).toBeTruthy()

    await user.selectOptions(screen.getByLabelText('Arama ölçütü'), 'phone')
    await user.clear(screen.getByLabelText('Aranan'))
    await user.type(screen.getByLabelText('Aranan'), '12345')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    expect(await screen.findByText('Türkiye’de bir cep telefonu numarası gir.')).toBeTruthy()
    expect(screen.queryByText('Eşleşen müşteri yok.')).toBeNull()
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

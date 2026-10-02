import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { fakeBff } from '../../test/fakeBff'
import { renderAt } from '../../test/render'
import type { StaffDetail, StaffPermission } from '../../types'

function signedIn(subject: string, ...permissions: StaffPermission[]) {
  return { status: 200, body: { subject, name: 'Yönetici', email: null, roles: permissions } }
}

const roles = {
  status: 200,
  body: {
    items: [
      { roleId: 'r1', name: 'Operasyon', description: null, permissions: ['customer.view', 'withdrawal.review'] },
      { roleId: 'r2', name: 'Pazarlama', description: null, permissions: ['promo.grant'] },
    ],
    first: 0,
    size: 100,
    nextFirst: null,
  },
}

function staff(overrides: Partial<StaffDetail>): StaffDetail {
  return {
    staffId: 's2',
    email: 'ayse@ornek.com',
    firstName: 'Ayşe',
    lastName: 'Yılmaz',
    enabled: true,
    invitationPending: false,
    createdAt: '2026-10-01T10:00:00Z',
    roles: [],
    ...overrides,
  }
}

describe('Personel', () => {
  it('yönetici çalışanı rolleriyle davet ediyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': signedIn('s1', 'staff.manage'),
      'GET /v1/roles?size=100': roles,
      'POST /v1/staff': { status: 201, body: staff({ invitationPending: true, roles: [{ roleId: 'r1', name: 'Operasyon' }] }) },
      'GET /v1/staff/s2': { status: 200, body: staff({ invitationPending: true, roles: [{ roleId: 'r1', name: 'Operasyon' }] }) },
    })

    renderAt('/personel/yeni', <App />)
    await userEvent.type(await screen.findByLabelText('E-posta'), 'ayse@ornek.com')
    await userEvent.type(screen.getByLabelText('Ad'), 'Ayşe')
    await userEvent.type(screen.getByLabelText('Soyad'), 'Yılmaz')
    await userEvent.click(await screen.findByLabelText(/Operasyon/))
    await userEvent.click(screen.getByRole('button', { name: 'Davet gönder' }))

    expect(await screen.findByText('Davet bekliyor')).toBeTruthy()
    expect(calls.find((call) => call.method === 'POST')?.body).toEqual({
      email: 'ayse@ornek.com',
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      roleIds: ['r1'],
    })
  })

  it('çalışanın rollerini değiştiriyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': signedIn('s1', 'staff.manage'),
      'GET /v1/roles?size=100': roles,
      'GET /v1/staff/s2': { status: 200, body: staff({}) },
      'PUT /v1/staff/s2/roles': { status: 200, body: staff({ roles: [{ roleId: 'r2', name: 'Pazarlama' }] }) },
    })

    renderAt('/personel/s2', <App />)
    await userEvent.click(await screen.findByLabelText(/Pazarlama/))
    await userEvent.click(screen.getByRole('button', { name: 'Rolleri kaydet' }))

    expect(await screen.findByText('Roller kaydedildi.')).toBeTruthy()
    expect(calls.find((call) => call.method === 'PUT')?.body).toEqual({ roleIds: ['r2'] })
  })

  /** Kendine yetki veremez: kendi hesabında rol ve kapatma yok, yalnızca açıklama. */
  it('kendi hesabında rolleri değiştirmeyi ve kapatmayı göstermiyor', async () => {
    fakeBff({
      'GET /bff/user': signedIn('s2', 'staff.manage'),
      'GET /v1/roles?size=100': roles,
      'GET /v1/staff/s2': { status: 200, body: staff({ roles: [{ roleId: 'r1', name: 'Operasyon' }] }) },
    })

    renderAt('/personel/s2', <App />)

    expect(await screen.findByText(/Kendi hesabın/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Rolleri kaydet' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Kapat' })).toBeNull()
  })

  it('çalışanı kapatıyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': signedIn('s1', 'staff.manage'),
      'GET /v1/roles?size=100': roles,
      'GET /v1/staff/s2': { status: 200, body: staff({}) },
      'POST /v1/staff/s2/disable': { status: 200, body: staff({ enabled: false }) },
    })

    renderAt('/personel/s2', <App />)
    await userEvent.click(await screen.findByRole('button', { name: 'Kapat' }))

    expect(await screen.findByText('Kapalı')).toBeTruthy()
    expect(calls.some((call) => call.path === '/v1/staff/s2/disable')).toBe(true)
  })

  it('menüde personel yalnızca personel yönetimi izniyle', async () => {
    fakeBff({ 'GET /bff/user': signedIn('s1', 'customer.view') })

    renderAt('/', <App />)

    expect(await screen.findByRole('link', { name: 'Çekimler' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Personel' })).toBeNull()
  })
})

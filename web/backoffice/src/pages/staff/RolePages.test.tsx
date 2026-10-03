import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { fakeBff, staffSession } from '../../test/fakeBff'
import { renderAt } from '../../test/render'

const admin = staffSession(['staff.manage'], { name: 'Yönetici' })

const permissions = {
  status: 200,
  body: [
    { name: 'customer.view', description: 'Müşteri kaydını görüntüler' },
    { name: 'withdrawal.review', description: 'İncelemedeki çekimi serbest bırakır ya da iptal eder' },
    { name: 'staff.manage', description: 'Personeli, rolleri ve rollerin izinlerini yönetir' },
  ],
}

const operations = {
  roleId: 'r1',
  name: 'Operasyon',
  description: 'Çekim incelemesi',
  permissions: ['customer.view'],
  members: [
    {
      staffId: 's3',
      email: 'can@ornek.com',
      firstName: 'Can',
      lastName: null,
      enabled: true,
      invitationPending: false,
      createdAt: '2026-10-01T10:00:00Z',
    },
  ],
}

describe('Roller', () => {
  it('izinleri seçerek rol açıyor', async () => {
    const calls = fakeBff({
      ...admin,
      'GET /v1/permissions': permissions,
      'POST /v1/roles': { status: 201, body: { roleId: 'r9', name: 'Destek', description: null, permissions: ['customer.view'] } },
      'GET /v1/roles/r9': {
        status: 200,
        body: { roleId: 'r9', name: 'Destek', description: null, permissions: ['customer.view'], members: [] },
      },
    })

    renderAt('/roller/yeni', <App />)
    await userEvent.type(await screen.findByLabelText('Ad'), 'Destek')
    await userEvent.click(await screen.findByLabelText(/Müşteri kaydını görüntüler/))
    await userEvent.click(screen.getByRole('button', { name: 'Rolü aç' }))

    expect(await screen.findByRole('heading', { name: 'Destek' })).toBeTruthy()
    expect(calls.find((call) => call.method === 'POST')?.body).toEqual({
      name: 'Destek',
      description: null,
      permissions: ['customer.view'],
    })
  })

  it('rolün izinlerini değiştiriyor ve üyelerini gösteriyor', async () => {
    const calls = fakeBff({
      ...admin,
      'GET /v1/permissions': permissions,
      'GET /v1/roles/r1': [
        { status: 200, body: operations },
        { status: 200, body: { ...operations, permissions: ['customer.view', 'withdrawal.review'] } },
      ],
      'PUT /v1/roles/r1': {
        status: 200,
        body: { roleId: 'r1', name: 'Operasyon', description: 'Çekim incelemesi', permissions: ['customer.view', 'withdrawal.review'] },
      },
    })

    renderAt('/roller/r1', <App />)
    expect(await screen.findByText('Can')).toBeTruthy()
    await userEvent.click(await screen.findByLabelText(/İncelemedeki çekimi/))
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Rol kaydedildi.')).toBeTruthy()
    expect(calls.find((call) => call.method === 'PUT')?.body).toEqual({
      description: 'Çekim incelemesi',
      permissions: ['customer.view', 'withdrawal.review'],
    })
  })

  it('kayıtta kimin ne yaptığını gösteriyor', async () => {
    fakeBff({
      ...admin,
      'GET /v1/audit-events': {
        status: 200,
        body: {
          items: [
            {
              eventId: 'e1',
              occurredAt: '2026-10-02T09:00:00Z',
              actorSubject: 's1',
              actorName: 'Fırat Ergül',
              action: 'staff_roles_changed',
              targetType: 'staff',
              targetId: 's2',
              targetLabel: 'ayse@ornek.com',
              details: { before: ['Destek'], after: ['Operasyon'] },
            },
          ],
          size: 20,
          nextCursor: null,
        },
      },
    })

    renderAt('/personel/kayitlar', <App />)

    expect(await screen.findByText('Rolleri değişti')).toBeTruthy()
    expect(screen.getByText('Fırat Ergül')).toBeTruthy()
    expect(screen.getByText('ayse@ornek.com')).toBeTruthy()
  })
})

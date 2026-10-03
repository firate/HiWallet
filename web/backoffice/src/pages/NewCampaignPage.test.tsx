import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { StaffRole } from '../types'

function staff(...roles: StaffRole[]) {
  return { status: 200, body: { subject: 's1', name: 'Çalışan', email: null, roles } }
}

describe('NewCampaignPage', () => {
  it('günlük ödeme toplamı kampanyasını tavanlarıyla açıyor', async () => {
    const calls = fakeBff({
      'GET /bff/user': staff('marketing'),
      'POST /v1/promo-campaigns': { status: 201, body: { campaignId: 'k1' } },
      'GET /v1/promo-campaigns/k1': {
        status: 200,
        body: {
          campaignId: 'k1',
          name: 'Ekim',
          rule: 'daily_payment_total',
          thresholdAmount: 500,
          rewardType: 'fixed',
          rewardAmount: 25,
          rewardRate: null,
          rewardMax: null,
          currency: 'TRY',
          grantScope: 'all_businesses',
          grantValidForDays: 30,
          budget: 10000,
          dailyCapPerAccount: 25,
          totalCapPerAccount: 100,
          startsAt: '2026-10-02T09:00:00Z',
          endsAt: null,
          triggerMerchantAccountIds: [],
          scopeMerchantAccountIds: [],
          granted: 0,
          createdAt: '2026-10-02T08:59:00Z',
          createdBy: 's1',
          endedBy: null,
        },
      },
    })

    renderAt('/kampanyalar/yeni', <App />)
    await userEvent.type(await screen.findByLabelText('Ad'), 'Ekim')
    await userEvent.selectOptions(screen.getByLabelText('Kural'), 'daily_payment_total')
    await userEvent.type(screen.getByLabelText('Günlük ödeme eşiği'), '500')
    await userEvent.type(screen.getByLabelText('Ödül tutarı'), '25')
    await userEvent.type(screen.getByLabelText('Partinin geçerliliği (gün)'), '30')
    await userEvent.type(screen.getByLabelText('Bütçe'), '10000')
    await userEvent.type(screen.getByLabelText('Hesap başına günlük tavan'), '25')
    await userEvent.type(screen.getByLabelText('Hesap başına toplam tavan'), '100')
    await userEvent.click(screen.getByRole('button', { name: 'Kampanyayı aç' }))

    expect(await screen.findByRole('heading', { name: 'Ekim' })).toBeTruthy()
    const body = calls.find((call) => call.method === 'POST')?.body as Record<string, unknown>
    expect(body).toMatchObject({
      name: 'Ekim',
      rule: 'daily_payment_total',
      thresholdAmount: 500,
      rewardType: 'fixed',
      rewardAmount: 25,
      rewardRate: null,
      rewardMax: null,
      currency: 'TRY',
      grantScope: 'all_businesses',
      grantValidForDays: 30,
      budget: 10000,
      dailyCapPerAccount: 25,
      totalCapPerAccount: 100,
      endsAt: null,
      triggerMerchantAccountIds: null,
      scopeMerchantAccountIds: null,
    })
    expect(typeof body.startsAt).toBe('string')
  })

  it('pazarlama rolü olmayan çalışana formu göstermiyor', async () => {
    fakeBff({ 'GET /bff/user': staff('operations') })

    renderAt('/kampanyalar/yeni', <App />)

    expect(await screen.findByText(/pazarlama rolü/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Kampanyayı aç' })).toBeNull()
  })
})

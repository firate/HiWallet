import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from '../App'
import { fakeBff, staffSession } from '../test/fakeBff'
import { renderAt } from '../test/render'
import type { Campaign } from '../types'

function campaign(overrides: Partial<Campaign>): Campaign {
  return {
    campaignId: 'k1',
    name: 'Ekim',
    rule: 'payment_to_merchant',
    thresholdAmount: null,
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
    endsAt: '2026-10-05T09:00:00Z',
    triggerMerchantAccountIds: [],
    scopeMerchantAccountIds: [],
    granted: 0,
    createdAt: '2026-10-02T08:59:00Z',
    createdBy: 's1',
    endedBy: 's2',
    ...overrides,
  }
}

describe('CampaignPage', () => {
  /** Açan ve bitiren adıyla; personel yönetiminde adı olmayan kimlik olduğu gibi. */
  it('açanı ve bitireni adıyla gösteriyor', async () => {
    fakeBff({
      ...staffSession(['campaign.view']),
      'GET /v1/promo-campaigns/k1': { status: 200, body: campaign({}) },
      'GET /v1/staff-names?subject=s1&subject=s2': {
        status: 200,
        body: { items: [{ subject: 's1', name: 'Ayşe Yılmaz' }] },
      },
    })

    renderAt('/kampanyalar/k1', <App />)

    expect(await screen.findByText('Ayşe Yılmaz')).toBeTruthy()
    expect(screen.getByText('s2')).toBeTruthy()
  })

  /** Backoffice öncesi açılan kampanyanın açanı yok; ad sorulmuyor. */
  it('açanı olmayan kampanyada ad sormuyor', async () => {
    const calls = fakeBff({
      ...staffSession(['campaign.view']),
      'GET /v1/promo-campaigns/k1': { status: 200, body: campaign({ createdBy: null, endedBy: null }) },
    })

    renderAt('/kampanyalar/k1', <App />)

    expect(await screen.findByRole('heading', { name: 'Ekim' })).toBeTruthy()
    expect(calls.some((call) => call.path.startsWith('/v1/staff-names'))).toBe(false)
  })
})

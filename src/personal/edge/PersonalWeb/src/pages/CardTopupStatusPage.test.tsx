import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { describe, expect, it } from 'vitest'
import { fakeBff } from '../test/fakeBff'
import { renderAt } from '../test/render'
import { CardTopupStatusPage } from './CardTopupStatusPage'

function cardTopup(state: string, failureReason: string | null = null) {
  return {
    cardTopupId: 'c1',
    walletId: 'w1',
    state,
    amount: 250,
    currency: 'TRY',
    paymentUrl: 'https://odeme.example/odeme/p1',
    expiresAt: '2026-10-06T10:15:00Z',
    failureReason,
    createdAt: '2026-10-06T10:00:00Z',
    updatedAt: '2026-10-06T10:01:00Z',
  }
}

/** Ödeme sayfası müşteriyi yüklemenin kimliğiyle buraya yolluyor. */
function renderStatus(path = '/kart-yukleme?cardTopupId=c1') {
  renderAt(
    path,
    <Routes>
      <Route path="kart-yukleme" element={<CardTopupStatusPage />} />
    </Routes>,
  )
}

describe('CardTopupStatusPage', () => {
  it('ödenen yüklemeyi ve cüzdana dönüşü gösteriyor', async () => {
    fakeBff({ 'GET /v1/card-topups/c1': { status: 200, body: cardTopup('paid') } })

    renderStatus()

    expect(await screen.findByText('Ödendi')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Cüzdana dön' }).getAttribute('href')).toBe('/cuzdanlar/w1')
  })

  /** Sağlayıcı vazgeçmeyi ve kartın reddini aynı sonuçla bildiriyor. */
  it('ödenmeyen yüklemenin sebebini söylüyor', async () => {
    fakeBff({ 'GET /v1/card-topups/c1': { status: 200, body: cardTopup('failed', 'canceled') } })

    renderStatus()

    expect(await screen.findByText('Ödeme tamamlanmadı: vazgeçildi ya da kart reddedildi.')).toBeTruthy()
  })

  it('ödeme beklenirken ödeme sayfasına dönmeyi sunuyor', async () => {
    fakeBff({ 'GET /v1/card-topups/c1': { status: 200, body: cardTopup('pending') } })

    renderStatus()

    const link = await screen.findByRole('link', { name: 'Ödeme sayfasına dön' })
    expect(link.getAttribute('href')).toBe('https://odeme.example/odeme/p1')
  })

  it('kimliksiz adreste yüklemeyi aramıyor', async () => {
    const calls = fakeBff({})

    renderStatus('/kart-yukleme')

    expect(await screen.findByText('Yükleme bulunamadı.')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })
})

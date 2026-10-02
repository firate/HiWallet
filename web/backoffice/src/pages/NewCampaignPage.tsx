import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { campaignRule, idList, promoScope } from '../format'
import { useHasPermission } from '../session'
import type { CampaignRequest, CampaignRule, PromoScope, RewardType } from '../types'

/**
 * Kampanya tanımı. Alanların birbiriyle uyumu wallet-api'de; form yalnızca kurala ve
 * ödüle göre gereken alanları gösteriyor. Eşik yalnızca günlük ödeme toplamında, yüzde
 * ödül ve tetikleyen işyerleri yalnızca işyerine ödemede.
 */
export function NewCampaignPage() {
  const allowed = useHasPermission('campaign.manage')

  if (!allowed) {
    return (
      <section className="card">
        <p>Kampanya açmak için kampanya yönetme izni gerekiyor.</p>
        <Link to="/kampanyalar">Kampanyalar</Link>
      </section>
    )
  }

  return <CampaignForm />
}

function CampaignForm() {
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [rule, setRule] = useState<CampaignRule>('payment_to_merchant')
  const [triggers, setTriggers] = useState('')
  const [threshold, setThreshold] = useState('')
  const [rewardType, setRewardType] = useState<RewardType>('fixed')
  const [rewardAmount, setRewardAmount] = useState('')
  const [rewardPercent, setRewardPercent] = useState('')
  const [rewardMax, setRewardMax] = useState('')
  const [currency, setCurrency] = useState('TRY')
  const [scope, setScope] = useState<PromoScope>('all_businesses')
  const [scopeMerchants, setScopeMerchants] = useState('')
  const [validDays, setValidDays] = useState('')
  const [budget, setBudget] = useState('')
  const [dailyCap, setDailyCap] = useState('')
  const [totalCap, setTotalCap] = useState('')
  const [startsAt, setStartsAt] = useState('')
  const [endsAt, setEndsAt] = useState('')

  // Günlük ödeme toplamında ödül hep sabit tutar.
  const effectiveRewardType: RewardType = rule === 'daily_payment_total' ? 'fixed' : rewardType

  const create = useMutation({
    mutationFn: () => {
      const request: CampaignRequest = {
        name: name.trim(),
        rule,
        thresholdAmount: rule === 'daily_payment_total' ? Number(threshold) : null,
        rewardType: effectiveRewardType,
        rewardAmount: effectiveRewardType === 'fixed' ? Number(rewardAmount) : null,
        rewardRate: effectiveRewardType === 'percentage' ? Number(rewardPercent) / 100 : null,
        rewardMax: effectiveRewardType === 'percentage' ? Number(rewardMax) : null,
        currency: currency.trim().toUpperCase(),
        grantScope: scope,
        grantValidForDays: validDays ? Number(validDays) : null,
        budget: Number(budget),
        dailyCapPerAccount: Number(dailyCap),
        totalCapPerAccount: Number(totalCap),
        // Boşsa şimdi: geçmişte başlayan kampanya açılışından önceki ödemeleri ödüllendirirdi.
        startsAt: startsAt ? new Date(startsAt).toISOString() : new Date().toISOString(),
        endsAt: endsAt ? new Date(endsAt).toISOString() : null,
        triggerMerchantAccountIds: rule === 'payment_to_merchant' ? idList(triggers) : null,
        scopeMerchantAccountIds: scope === 'selected_businesses' ? idList(scopeMerchants) : null,
      }

      return api.createCampaign(request)
    },
    onSuccess: (campaign) => navigate(`/kampanyalar/${campaign.campaignId}`),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    create.mutate()
  }

  return (
    <section className="card">
      <p>
        <Link to="/kampanyalar">Kampanyalar</Link>
      </p>
      <h1>Yeni kampanya</h1>
      <form className="stacked" onSubmit={submit}>
        <label>
          Ad
          <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={200} />
        </label>
        <label>
          Kural
          <select value={rule} onChange={(event) => setRule(event.target.value as CampaignRule)}>
            <option value="payment_to_merchant">{campaignRule('payment_to_merchant')}</option>
            <option value="daily_payment_total">{campaignRule('daily_payment_total')}</option>
          </select>
        </label>
        {rule === 'payment_to_merchant' ? (
          <label>
            Tetikleyen işyerleri (her satıra bir hesap kimliği)
            <textarea rows={3} value={triggers} onChange={(event) => setTriggers(event.target.value)} required />
          </label>
        ) : (
          <Amount label="Günlük ödeme eşiği" value={threshold} onChange={setThreshold} />
        )}
        {rule === 'payment_to_merchant' && (
          <label>
            Ödül türü
            <select value={rewardType} onChange={(event) => setRewardType(event.target.value as RewardType)}>
              <option value="fixed">Sabit tutar</option>
              <option value="percentage">Ödemenin yüzdesi</option>
            </select>
          </label>
        )}
        {effectiveRewardType === 'fixed' ? (
          <Amount label="Ödül tutarı" value={rewardAmount} onChange={setRewardAmount} />
        ) : (
          <>
            <label>
              Ödül oranı (%)
              <input
                type="number"
                min="0.01"
                max="100"
                step="0.01"
                value={rewardPercent}
                onChange={(event) => setRewardPercent(event.target.value)}
                required
              />
            </label>
            <Amount label="Ödül tavanı" value={rewardMax} onChange={setRewardMax} />
          </>
        )}
        <label>
          Para birimi
          <input value={currency} onChange={(event) => setCurrency(event.target.value)} required maxLength={3} />
        </label>
        <label>
          Partinin kapsamı
          <select value={scope} onChange={(event) => setScope(event.target.value as PromoScope)}>
            <option value="all_businesses">{promoScope('all_businesses')}</option>
            <option value="selected_businesses">{promoScope('selected_businesses')}</option>
          </select>
        </label>
        {scope === 'selected_businesses' && (
          <label>
            Kapsamdaki işyerleri (her satıra bir hesap kimliği)
            <textarea
              rows={3}
              value={scopeMerchants}
              onChange={(event) => setScopeMerchants(event.target.value)}
              required
            />
          </label>
        )}
        <label>
          Partinin geçerliliği (gün)
          <input
            type="number"
            min="1"
            step="1"
            value={validDays}
            onChange={(event) => setValidDays(event.target.value)}
            placeholder="Boşsa süresiz"
          />
        </label>
        <Amount label="Bütçe" value={budget} onChange={setBudget} />
        <Amount label="Hesap başına günlük tavan" value={dailyCap} onChange={setDailyCap} />
        <Amount label="Hesap başına toplam tavan" value={totalCap} onChange={setTotalCap} />
        <label>
          Başlangıç (boşsa şimdi)
          <input type="datetime-local" value={startsAt} onChange={(event) => setStartsAt(event.target.value)} />
        </label>
        <label>
          Bitiş (boşsa süresiz)
          <input type="datetime-local" value={endsAt} onChange={(event) => setEndsAt(event.target.value)} />
        </label>
        <button type="submit" disabled={create.isPending}>
          Kampanyayı aç
        </button>
      </form>
      {create.error && <ErrorMessage error={create.error} />}
    </section>
  )
}

function Amount({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label>
      {label}
      <input
        type="number"
        inputMode="decimal"
        min="0.01"
        step="0.01"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        required
      />
    </label>
  )
}

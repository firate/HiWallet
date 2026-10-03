import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { campaignRule, date, money, promoScope } from '../format'
import { useHasRole } from '../session'
import type { Campaign } from '../types'
import { hasEnded } from './CampaignsPage'

export function CampaignPage() {
  const { campaignId = '' } = useParams()
  const campaign = useQuery({ queryKey: ['campaigns', campaignId], queryFn: () => api.campaign(campaignId) })

  if (campaign.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (campaign.error) {
    return <ErrorMessage error={campaign.error} />
  }

  const c = campaign.data

  return (
    <>
      <p>
        <Link to="/kampanyalar">Kampanyalar</Link>
      </p>
      <section className="card">
        <h1>{c.name}</h1>
        <dl>
          <dt>Kural</dt>
          <dd>{campaignRule(c.rule)}</dd>
          {c.thresholdAmount !== null && (
            <>
              <dt>Günlük ödeme eşiği</dt>
              <dd>{money(c.thresholdAmount, c.currency)}</dd>
            </>
          )}
          <dt>Ödül</dt>
          <dd>{reward(c)}</dd>
          <dt>Partinin kapsamı</dt>
          <dd>{promoScope(c.grantScope)}</dd>
          <dt>Partinin geçerliliği</dt>
          <dd>{c.grantValidForDays === null ? 'süresiz' : `${c.grantValidForDays} gün`}</dd>
          <dt>Bütçe</dt>
          <dd>
            {money(c.granted, c.currency)} verildi / {money(c.budget, c.currency)}
          </dd>
          <dt>Hesap başına</dt>
          <dd>
            günde {money(c.dailyCapPerAccount, c.currency)}, toplam {money(c.totalCapPerAccount, c.currency)}
          </dd>
          <dt>Dönem</dt>
          <dd>
            {date(c.startsAt)} – {c.endsAt ? date(c.endsAt) : 'süresiz'}
          </dd>
          {c.triggerMerchantAccountIds.length > 0 && (
            <>
              <dt>Tetikleyen işyerleri</dt>
              <dd>
                <AccountLinks ids={c.triggerMerchantAccountIds} />
              </dd>
            </>
          )}
          {c.scopeMerchantAccountIds.length > 0 && (
            <>
              <dt>Kapsamdaki işyerleri</dt>
              <dd>
                <AccountLinks ids={c.scopeMerchantAccountIds} />
              </dd>
            </>
          )}
          <dt>Açan</dt>
          <dd>
            <code>{c.createdBy ?? '-'}</code>
          </dd>
          {c.endedBy && (
            <>
              <dt>Bitiren</dt>
              <dd>
                <code>{c.endedBy}</code>
              </dd>
            </>
          )}
        </dl>
        {!hasEnded(c) && <EndCampaign campaign={c} />}
      </section>
    </>
  )
}

function reward(c: Campaign): string {
  if (c.rewardType === 'percentage' && c.rewardRate !== null && c.rewardMax !== null) {
    return `ödemenin yüzdesi: %${(c.rewardRate * 100).toLocaleString('tr-TR')}, en fazla ${money(c.rewardMax, c.currency)}`
  }

  return c.rewardAmount === null ? '-' : money(c.rewardAmount, c.currency)
}

function AccountLinks({ ids }: { ids: string[] }) {
  return (
    <ul className="plain">
      {ids.map((id) => (
        <li key={id}>
          <Link to={`/hesaplar/${id}`}>
            <code>{id}</code>
          </Link>
        </li>
      ))}
    </ul>
  )
}

/** Kampanyayı şimdi bitirir; verilmiş partiler olduğu gibi kalıyor. Pazarlama rolü. */
function EndCampaign({ campaign }: { campaign: Campaign }) {
  const marketing = useHasRole('marketing')
  const queryClient = useQueryClient()
  const end = useMutation({
    mutationFn: () => api.endCampaign(campaign.campaignId),
    onSuccess: (updated) => {
      queryClient.setQueryData(['campaigns', campaign.campaignId], updated)
      void queryClient.invalidateQueries({ queryKey: ['campaigns', 'list'] })
    },
  })

  if (!marketing) {
    return null
  }

  return (
    <>
      <button type="button" className="secondary" onClick={() => end.mutate()} disabled={end.isPending}>
        Kampanyayı bitir
      </button>
      {end.error && <ErrorMessage error={end.error} />}
    </>
  )
}

import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { campaignRule, date, money } from '../format'
import { useHasPermission } from '../session'
import type { Campaign } from '../types'

/** Promo kampanyaları, yeniden eskiye. Açmak ve bitirmek kampanya yönetme izniyle. */
export function CampaignsPage() {
  const canManage = useHasPermission('campaign.manage')
  const campaigns = useInfiniteQuery({
    queryKey: ['campaigns', 'list'],
    queryFn: ({ pageParam }) => api.campaigns(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  const items = campaigns.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <section className="card">
      <div className="heading">
        <h1>Kampanyalar</h1>
        {canManage && (
          <Link className="button" to="/kampanyalar/yeni">
            Yeni kampanya
          </Link>
        )}
      </div>
      {campaigns.isPending && <p className="muted">Yükleniyor...</p>}
      {campaigns.error && <ErrorMessage error={campaigns.error} />}
      {campaigns.isSuccess && items.length === 0 && <p className="muted">Kampanya yok.</p>}
      {items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Ad</th>
              <th>Kural</th>
              <th>Dönem</th>
              <th className="amount">Verilen / bütçe</th>
            </tr>
          </thead>
          <tbody>
            {items.map((campaign) => (
              <tr key={campaign.campaignId}>
                <td>
                  <Link to={`/kampanyalar/${campaign.campaignId}`}>{campaign.name}</Link>
                  {hasEnded(campaign) && <span className="muted small"> (bitti)</span>}
                </td>
                <td>{campaignRule(campaign.rule)}</td>
                <td className="small">
                  {date(campaign.startsAt)} – {campaign.endsAt ? date(campaign.endsAt) : 'süresiz'}
                </td>
                <td className="amount">
                  {money(campaign.granted, campaign.currency)} / {money(campaign.budget, campaign.currency)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {campaigns.hasNextPage && (
        <button className="secondary" onClick={() => campaigns.fetchNextPage()} disabled={campaigns.isFetchingNextPage}>
          Daha eski kampanyalar
        </button>
      )}
    </section>
  )
}

export function hasEnded(campaign: Campaign): boolean {
  return campaign.endsAt !== null && new Date(campaign.endsAt) <= new Date()
}

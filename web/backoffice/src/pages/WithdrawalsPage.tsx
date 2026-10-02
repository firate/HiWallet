import { useInfiniteQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, money, withdrawalState, withdrawalStates } from '../format'

/** Bir durumdaki çekimler, en eski önce. Varsayılan inceleme kuyruğu. */
export function WithdrawalsPage() {
  const [params, setParams] = useSearchParams()
  const state = params.get('durum') ?? 'under_review'

  const withdrawals = useInfiniteQuery({
    queryKey: ['withdrawals', 'list', state],
    queryFn: ({ pageParam }) => api.withdrawals(state, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  const items = withdrawals.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <section className="card">
      <div className="heading">
        <h1>Çekimler</h1>
        <select aria-label="Durum" value={state} onChange={(event) => setParams({ durum: event.target.value })}>
          {Object.entries(withdrawalStates).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </div>
      {withdrawals.isPending && <p className="muted">Yükleniyor...</p>}
      {withdrawals.error && <ErrorMessage error={withdrawals.error} />}
      {withdrawals.isSuccess && items.length === 0 && <p className="muted">Bu durumda çekim yok.</p>}
      {items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Başlatıldı</th>
              <th>IBAN</th>
              <th>Durum</th>
              <th className="amount">Tutar</th>
            </tr>
          </thead>
          <tbody>
            {items.map((withdrawal) => (
              <tr key={withdrawal.withdrawalId}>
                <td>
                  <Link to={`/cekimler/${withdrawal.withdrawalId}`}>{date(withdrawal.createdAt)}</Link>
                </td>
                <td>{withdrawal.destinationIban}</td>
                <td>{withdrawalState(withdrawal.state)}</td>
                <td className="amount">{money(withdrawal.amount, withdrawal.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {withdrawals.hasNextPage && (
        <button
          className="secondary"
          onClick={() => withdrawals.fetchNextPage()}
          disabled={withdrawals.isFetchingNextPage}
        >
          Devamı
        </button>
      )}
    </section>
  )
}

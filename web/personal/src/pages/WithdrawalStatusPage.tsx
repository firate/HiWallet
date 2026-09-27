import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, isFinalWithdrawalState, money, withdrawalState } from '../format'

const pollInterval = 2000

/** Çekimin durumu. Sonuç kesinleşene kadar birkaç saniyede bir soruluyor. */
export function WithdrawalStatusPage() {
  const { withdrawalId = '' } = useParams()
  const withdrawal = useQuery({
    queryKey: ['withdrawals', withdrawalId],
    queryFn: () => api.withdrawal(withdrawalId),
    refetchInterval: (query) =>
      query.state.data && isFinalWithdrawalState(query.state.data.state) ? false : pollInterval,
  })

  if (withdrawal.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (withdrawal.error) {
    return <ErrorMessage error={withdrawal.error} />
  }

  const { state, amount, currency, destinationIban, totalDebited, failureReason, createdAt, updatedAt } =
    withdrawal.data

  return (
    <>
      <p>
        <Link to="/">Cüzdanlarım</Link>
      </p>
      <section className="card">
        <h1>Para çekme</h1>
        <p className="balance">{money(amount, currency)}</p>
        <p>
          Durum: <strong>{withdrawalState(state)}</strong>
          {!isFinalWithdrawalState(state) && <span className="muted"> (izleniyor)</span>}
        </p>
        <dl>
          <dt>IBAN</dt>
          <dd>{destinationIban}</dd>
          {totalDebited !== null && (
            <>
              <dt>Cüzdandan düşülen (komisyon dahil)</dt>
              <dd>{money(totalDebited, currency)}</dd>
            </>
          )}
          {failureReason && (
            <>
              <dt>Sebep</dt>
              <dd>{failureReason}</dd>
            </>
          )}
          <dt>Başlatıldı</dt>
          <dd>{date(createdAt)}</dd>
          <dt>Son güncelleme</dt>
          <dd>{date(updatedAt)}</dd>
        </dl>
      </section>
    </>
  )
}

import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { cardTopupFailure, cardTopupState, date, isFinalCardTopupState, money } from '../format'

const pollInterval = 2000

/**
 * Ödeme sayfasından dönüş. Kart sağlayıcısı müşteriyi yüklemenin kimliğiyle buraya
 * yolluyor, sonucu ise bize ayrı bir bildirimle gönderiyor: dönüş anında sonuç henüz
 * gelmemiş olabilir. Kesinleşene kadar birkaç saniyede bir soruluyor.
 */
export function CardTopupStatusPage() {
  const [params] = useSearchParams()
  const cardTopupId = params.get('cardTopupId') ?? ''
  const topup = useQuery({
    queryKey: ['card-topups', cardTopupId],
    queryFn: () => api.cardTopup(cardTopupId),
    enabled: cardTopupId !== '',
    refetchInterval: (query) => {
      const state = query.state.data?.state
      return state && isFinalCardTopupState(state) ? false : pollInterval
    },
  })

  const back = (
    <p>
      <Link to="/">Cüzdanlarım</Link>
    </p>
  )

  if (cardTopupId === '') {
    return (
      <>
        {back}
        <p>Yükleme bulunamadı.</p>
      </>
    )
  }

  if (topup.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (topup.error) {
    return <ErrorMessage error={topup.error} />
  }

  const { walletId, state, amount, currency, paymentUrl, failureReason, createdAt, updatedAt } = topup.data

  return (
    <>
      {back}
      <section className="card">
        <h1>Kartla para yükleme</h1>
        <p className="balance">{money(amount, currency)}</p>
        <p>
          Durum: <strong>{cardTopupState(state)}</strong>
          {!isFinalCardTopupState(state) && <span className="muted"> (izleniyor)</span>}
        </p>
        {state === 'paid' && <p>Ödeme alındı; para birkaç saniye içinde cüzdanında.</p>}
        {failureReason && <p>{cardTopupFailure(failureReason)}</p>}
        {state === 'pending' && paymentUrl && (
          <p>
            <a href={paymentUrl}>Ödeme sayfasına dön</a>
          </p>
        )}
        <dl>
          <dt>Başlatıldı</dt>
          <dd>{date(createdAt)}</dd>
          <dt>Son güncelleme</dt>
          <dd>{date(updatedAt)}</dd>
        </dl>
        <Link className="button" to={`/cuzdanlar/${walletId}`}>
          Cüzdana dön
        </Link>
      </section>
    </>
  )
}

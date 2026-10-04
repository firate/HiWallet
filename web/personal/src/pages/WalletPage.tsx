import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, fundType, money, movementType } from '../format'

/** Cüzdanın bakiyesi, hareketleri ve promo partileri. */
export function WalletPage() {
  const { walletId = '' } = useParams()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })

  if (wallet.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (wallet.error) {
    return <ErrorMessage error={wallet.error} />
  }

  const { name, currency, balance, withdrawable, balances } = wallet.data

  return (
    <>
      <p>
        <Link to="/">Cüzdanlarım</Link>
      </p>
      <section className="card">
        <h1>{name}</h1>
        <p className="balance">{money(balance, currency)}</p>
        <p className="muted">Çekilebilir: {money(withdrawable, currency)}</p>
        <ul className="buckets">
          {balances.map((bucket) => (
            <li key={bucket.fundType}>
              {fundType(bucket.fundType)}: {money(bucket.balance, currency)}
            </li>
          ))}
        </ul>
        <div className="actions">
          <Link className="button" to={`/cuzdanlar/${walletId}/transfer`}>
            Para gönder
          </Link>
          <Link className="button secondary" to={`/cuzdanlar/${walletId}/cekim`}>
            Banka hesabına çek
          </Link>
        </div>
      </section>
      <Movements walletId={walletId} />
      <Promos walletId={walletId} />
    </>
  )
}

function Movements({ walletId }: { walletId: string }) {
  const movements = useInfiniteQuery({
    queryKey: ['wallets', walletId, 'movements'],
    queryFn: ({ pageParam }) => api.movements(walletId, pageParam),
    initialPageParam: null as number | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  if (movements.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (movements.error) {
    return <ErrorMessage error={movements.error} />
  }

  const items = movements.data.pages.flatMap((page) => page.items)

  return (
    <section className="card">
      <h2>Hareketler</h2>
      {items.length === 0 ? (
        <p className="muted">Henüz hareket yok.</p>
      ) : (
        <table>
          <tbody>
            {items.map((movement) => (
              <tr key={movement.movementId}>
                <td>
                  {movementType(movement.type)}
                  <span className="muted"> ({fundType(movement.fundType)})</span>
                  <div className="muted small">{date(movement.createdAt)}</div>
                </td>
                <td className={movement.amount < 0 ? 'amount out' : 'amount in'}>
                  {money(movement.amount, movement.currency)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {movements.hasNextPage && (
        <button className="secondary" onClick={() => movements.fetchNextPage()} disabled={movements.isFetchingNextPage}>
          Daha eski hareketler
        </button>
      )}
    </section>
  )
}

function Promos({ walletId }: { walletId: string }) {
  const promos = useQuery({ queryKey: ['wallets', walletId, 'promos'], queryFn: () => api.promos(walletId) })

  if (promos.isPending || promos.error || promos.data.items.length === 0) {
    return null
  }

  return (
    <section className="card">
      <h2>Promo</h2>
      <p className="muted">Promo yalnızca işyerine ödemede harcanıyor.</p>
      <table>
        <tbody>
          {promos.data.items.map((promo) => (
            <tr key={promo.grantId}>
              <td>
                {money(promo.remaining, promo.currency)} kaldı
                <div className="muted small">
                  {money(promo.amount, promo.currency)} yüklendi,{' '}
                  {promo.expired ? 'süresi doldu' : promo.expiresAt ? `son gün ${date(promo.expiresAt)}` : 'süresiz'}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}

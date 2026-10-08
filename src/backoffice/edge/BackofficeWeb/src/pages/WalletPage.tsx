import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, fundType, money, movementType, promoScope, withdrawalState } from '../format'
import { useIdempotencyKey } from '../idempotency'
import { merchantIds } from '../merchants'
import { useHasPermission } from '../session'
import type { PromoGrant, PromoScope, Wallet } from '../types'

/** Cüzdanın bakiyesi, hareketleri ve promo partileri; personel promo'su da buradan veriliyor. */
export function WalletPage() {
  const { walletId = '' } = useParams()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })

  if (wallet.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (wallet.error) {
    return <ErrorMessage error={wallet.error} />
  }

  const { name, accountId, currency, balance, withdrawable, balances } = wallet.data

  return (
    <>
      <p>
        <Link to={`/hesaplar/${accountId}`}>Hesaba dön</Link>
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
        <p className="muted small">
          Cüzdan <code>{walletId}</code>
        </p>
      </section>
      <StaffPromoForm wallet={wallet.data} />
      <Movements walletId={walletId} />
      <Withdrawals walletId={walletId} />
      <Promos walletId={walletId} />
    </>
  )
}

/**
 * Personel promo'su: platform fonlu, tek seferlik tavanı wallet-api'de. Ledger'da aktör
 * bu çalışan. Anahtar başarılı cevaba kadar aynı kalıyor; cevabı gelmeyen istek tekrar
 * gönderilirse ikinci parti açılmıyor.
 */
function StaffPromoForm({ wallet }: { wallet: Wallet }) {
  const allowed = useHasPermission('promo.grant')
  const queryClient = useQueryClient()
  const [key, renewKey] = useIdempotencyKey()
  const [amount, setAmount] = useState('')
  const [scope, setScope] = useState<PromoScope>('all_businesses')
  const [merchants, setMerchants] = useState('')
  const [expiresOn, setExpiresOn] = useState('')
  const [granted, setGranted] = useState<PromoGrant | null>(null)

  const grant = useMutation({
    mutationFn: async () =>
      api.grantStaffPromo(
        wallet.walletId,
        {
          amount: Number(amount),
          currency: wallet.currency,
          scope,
          merchantAccountIds: scope === 'selected_businesses' ? await merchantIds(merchants) : null,
          // Seçilen günün sonuna kadar, panelin saatine göre.
          expiresAt: expiresOn ? new Date(`${expiresOn}T23:59:59`).toISOString() : null,
        },
        key,
      ),
    onSuccess: async (result) => {
      setGranted(result)
      setAmount('')
      renewKey()
      await queryClient.invalidateQueries({ queryKey: ['wallets', wallet.walletId] })
    },
  })

  if (!allowed) {
    return null
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    setGranted(null)
    grant.mutate()
  }

  return (
    <section className="card">
      <h2>Personel promo'su</h2>
      <form className="stacked" onSubmit={submit}>
        <label>
          Tutar
          <input
            type="number"
            inputMode="decimal"
            min="0.01"
            step="0.01"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            required
          />
        </label>
        <label>
          Kapsam
          <select value={scope} onChange={(event) => setScope(event.target.value as PromoScope)}>
            <option value="all_businesses">{promoScope('all_businesses')}</option>
            <option value="selected_businesses">{promoScope('selected_businesses')}</option>
          </select>
        </label>
        {scope === 'selected_businesses' && (
          <label>
            İşyerleri (her satıra bir hesap numarası ya da kimlik)
            <textarea rows={3} value={merchants} onChange={(event) => setMerchants(event.target.value)} required />
          </label>
        )}
        <label>
          Son gün (boşsa süresiz)
          <input type="date" value={expiresOn} onChange={(event) => setExpiresOn(event.target.value)} />
        </label>
        <button type="submit" disabled={grant.isPending}>
          Promo ver
        </button>
        <p className="muted small">Tek seferde verilebilecek tutarın tavanı var; üstü kampanyayla verilir.</p>
      </form>
      {grant.error && <ErrorMessage error={grant.error} />}
      {granted && (
        <div className="success">
          <p>{granted.replayed ? 'Bu istek daha önce işlenmişti; yeni parti açılmadı.' : 'Promo verildi.'}</p>
        </div>
      )}
    </section>
  )
}

/** Bu cüzdandan başlatılan bütün çekimler, yeniden eskiye; her biri çekimin sayfasına gidiyor. */
function Withdrawals({ walletId }: { walletId: string }) {
  const withdrawals = useInfiniteQuery({
    queryKey: ['wallets', walletId, 'withdrawals'],
    queryFn: ({ pageParam }) => api.walletWithdrawals(walletId, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  if (withdrawals.isPending) {
    return null
  }

  if (withdrawals.error) {
    return <ErrorMessage error={withdrawals.error} />
  }

  const items = withdrawals.data.pages.flatMap((page) => page.items)

  return (
    <section className="card">
      <h2>Çekimler</h2>
      {items.length === 0 ? (
        <p className="muted">Bu cüzdandan çekim yapılmadı.</p>
      ) : (
        <table>
          <tbody>
            {items.map((withdrawal) => (
              <tr key={withdrawal.withdrawalId}>
                <td>
                  <Link to={`/cekimler/${withdrawal.withdrawalId}`}>{withdrawalState(withdrawal.state)}</Link>
                  <div className="muted small">
                    {withdrawal.destinationIban}, {date(withdrawal.createdAt)}
                  </div>
                </td>
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
          Daha eski çekimler
        </button>
      )}
    </section>
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
        <p className="muted">Hareket yok.</p>
      ) : (
        <table>
          <tbody>
            {items.map((movement) => (
              <tr key={movement.movementId}>
                <td>
                  {movementType(movement.type)}
                  <span className="muted"> ({fundType(movement.fundType)})</span>
                  <div className="muted small">
                    {date(movement.createdAt)} · <code>{movement.transactionId}</code>
                  </div>
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
  const promos = useInfiniteQuery({
    queryKey: ['wallets', walletId, 'promos'],
    queryFn: ({ pageParam }) => api.promos(walletId, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  if (promos.isPending) {
    return null
  }

  if (promos.error) {
    return <ErrorMessage error={promos.error} />
  }

  const items = promos.data.pages.flatMap((page) => page.items)

  return (
    <section className="card">
      <h2>Promo partileri</h2>
      {items.length === 0 ? (
        <p className="muted">Promo partisi yok.</p>
      ) : (
        <table>
          <tbody>
            {items.map((promo) => (
              <tr key={promo.grantId}>
                <td>
                  {money(promo.remaining, promo.currency)} kaldı
                  <div className="muted small">
                    {money(promo.amount, promo.currency)} · fonlayan {promo.funder} · {promoScope(promo.scope)} ·{' '}
                    {promo.expired ? 'süresi doldu' : promo.expiresAt ? `son gün ${date(promo.expiresAt)}` : 'süresiz'}
                  </div>
                </td>
                <td className="small muted">{date(promo.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {promos.hasNextPage && (
        <button className="secondary" onClick={() => promos.fetchNextPage()} disabled={promos.isFetchingNextPage}>
          Daha eski partiler
        </button>
      )}
    </section>
  )
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, money, withdrawalState } from '../format'
import { useHasPermission } from '../session'
import type { Withdrawal } from '../types'

/** Çekimin durumu; incelemedeyse buradan serbest bırakılıyor ya da iptal ediliyor. */
export function WithdrawalPage() {
  const { withdrawalId = '' } = useParams()
  const withdrawal = useQuery({
    queryKey: ['withdrawals', withdrawalId],
    queryFn: () => api.withdrawal(withdrawalId),
  })

  if (withdrawal.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (withdrawal.error) {
    return <ErrorMessage error={withdrawal.error} />
  }

  const w = withdrawal.data

  return (
    <>
      <p>
        <Link to="/cekimler">Çekimler</Link>
      </p>
      <section className="card">
        <h1>Çekim</h1>
        <dl>
          <dt>Durum</dt>
          <dd>{withdrawalState(w.state)}</dd>
          <dt>Tutar</dt>
          <dd>{money(w.amount, w.currency)}</dd>
          {w.totalDebited !== null && (
            <>
              <dt>Cüzdandan çıkan</dt>
              <dd>{money(w.totalDebited, w.currency)}</dd>
            </>
          )}
          <dt>IBAN</dt>
          <dd>{w.destinationIban}</dd>
          {w.failureReason && (
            <>
              <dt>Sebep</dt>
              <dd>{w.failureReason}</dd>
            </>
          )}
          <dt>Hesap</dt>
          <dd>
            <Link to={`/hesaplar/${w.accountId}`}>
              <code>{w.accountId}</code>
            </Link>
          </dd>
          <dt>Cüzdan</dt>
          <dd>
            <Link to={`/cuzdanlar/${w.walletId}`}>
              <code>{w.walletId}</code>
            </Link>
          </dd>
          <dt>Başlatıldı</dt>
          <dd>{date(w.createdAt)}</dd>
          <dt>Son değişiklik</dt>
          <dd>{date(w.updatedAt)}</dd>
        </dl>
      </section>
      {w.state === 'under_review' && <Review withdrawal={w} />}
    </>
  )
}

/**
 * Eşiğin üstündeki çekim düşüldükten sonra bankaya gitmeden bekliyor. Serbest bırakmak
 * banka komutunu gönderiyor; iptal parayı cüzdana geri veriyor ve ters kaydın aktörü
 * iptal eden çalışan. Karar çekim inceleme izniyle.
 */
function Review({ withdrawal }: { withdrawal: Withdrawal }) {
  const allowed = useHasPermission('withdrawal.review')
  const queryClient = useQueryClient()
  const [reason, setReason] = useState('')

  function show(updated: Withdrawal) {
    queryClient.setQueryData(['withdrawals', withdrawal.withdrawalId], updated)
    void queryClient.invalidateQueries({ queryKey: ['withdrawals', 'list'] })
  }

  const release = useMutation({ mutationFn: () => api.releaseWithdrawal(withdrawal.withdrawalId), onSuccess: show })
  const cancel = useMutation({
    mutationFn: () => api.cancelWithdrawal(withdrawal.withdrawalId, reason.trim()),
    onSuccess: show,
  })

  if (!allowed) {
    return (
      <section className="card">
        <p className="muted">Bu çekim incelemede. Serbest bırakmak ve iptal etmek için çekim inceleme izni gerekiyor.</p>
      </section>
    )
  }

  const busy = release.isPending || cancel.isPending

  function submitCancel(event: FormEvent) {
    event.preventDefault()
    cancel.mutate()
  }

  return (
    <section className="card">
      <h2>İnceleme</h2>
      <div className="actions">
        <button type="button" onClick={() => release.mutate()} disabled={busy}>
          Serbest bırak
        </button>
      </div>
      {release.error && <ErrorMessage error={release.error} />}
      <form className="stacked spaced" onSubmit={submitCancel}>
        <label>
          İptal sebebi
          <input value={reason} onChange={(event) => setReason(event.target.value)} required maxLength={500} />
        </label>
        <p className="muted small">Sebep müşteriye çekimin durumunda gösteriliyor.</p>
        <button type="submit" className="secondary" disabled={busy}>
          İptal et
        </button>
      </form>
      {cancel.error && <ErrorMessage error={cancel.error} />}
    </section>
  )
}

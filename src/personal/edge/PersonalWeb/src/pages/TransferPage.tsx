import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { money } from '../format'
import { useIdempotencyKey } from '../idempotency'
import { blockedReason, useLimits } from '../limits'
import type { TransferResponse, TransferType } from '../types'

/**
 * Bir kişiye para gönderme ya da işyerine ödeme. Alıcı hesap numarasıyla; para
 * alıcının bu para birimindeki varsayılan cüzdanına düşüyor.
 */
export function TransferPage() {
  const { walletId = '' } = useParams()
  const queryClient = useQueryClient()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })
  const accountId = wallet.data?.accountId
  const account = useQuery({
    queryKey: ['accounts', accountId],
    queryFn: () => api.account(accountId ?? ''),
    enabled: accountId !== undefined,
  })
  const limits = useLimits(accountId, account.data?.kycLevel, wallet.data?.currency)

  const [toAccountNumber, setToAccountNumber] = useState('')
  const [amount, setAmount] = useState('')
  const [chosenType, setType] = useState<TransferType>('P2P')
  const [idempotencyKey, renewIdempotencyKey] = useIdempotencyKey()
  const [done, setDone] = useState<TransferResponse | null>(null)

  // Seviyenin kapattığı tür seçilemiyor; kişiye gönderim kapalıysa form işyerine ödemeyle açılıyor.
  const p2pBlocked = blockedReason(limits.data, 'OutgoingTransfer')
  const paymentBlocked = blockedReason(limits.data, 'Payment')
  const type: TransferType = chosenType === 'P2P' && p2pBlocked && !paymentBlocked ? 'Payment' : chosenType

  const transfer = useMutation({
    mutationFn: (currency: string) =>
      api.transfer(
        { fromWalletId: walletId, toAccountNumber: toAccountNumber.trim(), amount: Number(amount), currency, type },
        idempotencyKey,
      ),
    onSuccess: async (response) => {
      setDone(response)
      setToAccountNumber('')
      setAmount('')
      renewIdempotencyKey()
      await queryClient.invalidateQueries({ queryKey: ['wallets', walletId] })
    },
  })

  if (wallet.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (wallet.error) {
    return <ErrorMessage error={wallet.error} />
  }

  const { name, currency, balance, balances } = wallet.data
  const promo = balances.find((bucket) => bucket.fundType === 'promo')?.balance ?? 0

  function submit(event: FormEvent) {
    event.preventDefault()
    setDone(null)
    transfer.mutate(currency)
  }

  const back = (
    <p>
      <Link to={`/cuzdanlar/${walletId}`}>{name}</Link>
    </p>
  )

  if (p2pBlocked && paymentBlocked) {
    return (
      <>
        {back}
        <section className="card">
          <h1>Para gönder</h1>
          <div className="notice">
            <p>Şu an para gönderemiyorsun: {paymentBlocked}</p>
            <Link to={`/hesaplar/${wallet.data.accountId}/limitler`}>Limitlerim</Link>
          </div>
        </section>
      </>
    )
  }

  return (
    <>
      {back}
      <section className="card">
        <h1>Para gönder</h1>
        <p className="muted">
          Bakiye: {money(balance, currency)}
          {promo > 0 && <>. Bunun {money(promo, currency)} kadarı promo ve yalnızca işyerine ödemede harcanıyor.</>}
        </p>
        <form className="stacked" onSubmit={submit}>
          <label>
            Ne için
            <select value={type} onChange={(event) => setType(event.target.value as TransferType)}>
              <option value="P2P" disabled={p2pBlocked !== null}>
                Bir kişiye{p2pBlocked && ' (kapalı)'}
              </option>
              <option value="Payment" disabled={paymentBlocked !== null}>
                İşyerine ödeme{paymentBlocked && ' (kapalı)'}
              </option>
            </select>
          </label>
          {p2pBlocked && <p className="muted small">Başka birine gönderemiyorsun: {p2pBlocked}</p>}
          <label>
            Alıcının hesap numarası
            <input
              inputMode="numeric"
              autoComplete="off"
              placeholder="123 456 7890"
              value={toAccountNumber}
              onChange={(event) => setToAccountNumber(event.target.value)}
              required
            />
          </label>
          <label>
            Tutar ({currency})
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
          <button type="submit" disabled={transfer.isPending}>
            Gönder
          </button>
        </form>
        {transfer.error && <ErrorMessage error={transfer.error} />}
        {done && (
          <p className="success" role="status">
            {done.replayed ? 'Bu gönderim daha önce yapılmıştı; tekrar gönderilmedi.' : 'Gönderildi.'}
          </p>
        )}
      </section>
    </>
  )
}

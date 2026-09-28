import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { money } from '../format'
import { useIdempotencyKey } from '../idempotency'
import type { TransferResponse, TransferType } from '../types'

/** Başka bir cüzdana para gönderme ya da işyerine ödeme. */
export function TransferPage() {
  const { walletId = '' } = useParams()
  const queryClient = useQueryClient()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })

  const [toWalletId, setToWalletId] = useState('')
  const [amount, setAmount] = useState('')
  const [type, setType] = useState<TransferType>('P2P')
  const [idempotencyKey, renewIdempotencyKey] = useIdempotencyKey()
  const [done, setDone] = useState<TransferResponse | null>(null)

  const transfer = useMutation({
    mutationFn: (currency: string) =>
      api.transfer(
        { fromWalletId: walletId, toWalletId: toWalletId.trim(), amount: Number(amount), currency, type },
        idempotencyKey,
      ),
    onSuccess: async (response) => {
      setDone(response)
      setToWalletId('')
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

  return (
    <>
      <p>
        <Link to={`/cuzdanlar/${walletId}`}>{name}</Link>
      </p>
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
              <option value="P2P">Bir kişiye</option>
              <option value="Payment">İşyerine ödeme</option>
            </select>
          </label>
          <label>
            Alıcının cüzdan numarası
            <input value={toWalletId} onChange={(event) => setToWalletId(event.target.value)} required />
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

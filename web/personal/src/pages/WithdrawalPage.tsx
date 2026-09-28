import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { money } from '../format'
import { useIdempotencyKey } from '../idempotency'

/**
 * IBAN'a para çekme. Cevap "istek alındı" demek, para henüz çıkmadı; sonuç çekimin
 * sayfasından izleniyor.
 */
export function WithdrawalPage() {
  const { walletId = '' } = useParams()
  const navigate = useNavigate()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })

  const [amount, setAmount] = useState('')
  const [iban, setIban] = useState('')
  const [idempotencyKey] = useIdempotencyKey()

  const withdraw = useMutation({
    mutationFn: (currency: string) =>
      api.withdraw({ walletId, amount: Number(amount), currency, destinationIban: iban }, idempotencyKey),
    onSuccess: (response) => navigate(`/cekimler/${response.withdrawalId}`),
  })

  if (wallet.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (wallet.error) {
    return <ErrorMessage error={wallet.error} />
  }

  const { name, currency, withdrawable } = wallet.data

  function submit(event: FormEvent) {
    event.preventDefault()
    withdraw.mutate(currency)
  }

  return (
    <>
      <p>
        <Link to={`/cuzdanlar/${walletId}`}>{name}</Link>
      </p>
      <section className="card">
        <h1>Banka hesabına çek</h1>
        <p className="muted">
          Çekilebilir: {money(withdrawable, currency)}. Komisyon tutara ek olarak cüzdandan düşülüyor.
        </p>
        <form className="stacked" onSubmit={submit}>
          <label>
            IBAN
            <input value={iban} onChange={(event) => setIban(event.target.value)} placeholder="TR.." required />
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
          <button type="submit" disabled={withdraw.isPending}>
            Çekimi başlat
          </button>
        </form>
        {withdraw.error && <ErrorMessage error={withdraw.error} />}
      </section>
    </>
  )
}

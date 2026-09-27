import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { money } from '../format'

const currencies = ['TRY', 'USD', 'EUR']

/** Hesaplar ve altındaki cüzdanlar. Hesabı olmayan müşteri buradan açıyor. */
export function AccountsPage() {
  const queryClient = useQueryClient()
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.accounts })
  const openAccount = useMutation({
    mutationFn: api.openAccount,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts'] }),
  })

  if (accounts.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (accounts.error) {
    return <ErrorMessage error={accounts.error} />
  }

  if (accounts.data.items.length === 0) {
    return (
      <section className="card">
        <h1>Hoş geldin</h1>
        <p>Henüz bir hesabın yok. Hesap açınca içine cüzdan açabilirsin.</p>
        <button onClick={() => openAccount.mutate()} disabled={openAccount.isPending}>
          Hesap aç
        </button>
        {openAccount.error && <ErrorMessage error={openAccount.error} />}
      </section>
    )
  }

  return (
    <>
      <h1>Cüzdanlarım</h1>
      {accounts.data.items.map((account) => (
        <AccountCard key={account.accountId} accountId={account.accountId} />
      ))}
    </>
  )
}

function AccountCard({ accountId }: { accountId: string }) {
  const account = useQuery({ queryKey: ['accounts', accountId], queryFn: () => api.account(accountId) })

  if (account.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  return (
    <section className="card">
      {account.data.wallets.length === 0 ? (
        <p className="muted">Bu hesapta henüz cüzdan yok.</p>
      ) : (
        <ul className="wallets">
          {account.data.wallets.map((wallet) => (
            <li key={wallet.walletId}>
              <Link to={`/cuzdanlar/${wallet.walletId}`}>
                <span className="name">{wallet.name}</span>
                <span className="amount">{money(wallet.balance, wallet.currency)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      <OpenWalletForm accountId={accountId} />
    </section>
  )
}

function OpenWalletForm({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [currency, setCurrency] = useState(currencies[0])
  const openWallet = useMutation({
    mutationFn: () => api.openWallet(accountId, name.trim(), currency),
    onSuccess: async () => {
      setName('')
      await queryClient.invalidateQueries({ queryKey: ['accounts', accountId] })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    openWallet.mutate()
  }

  return (
    <form className="inline" onSubmit={submit}>
      <input
        aria-label="Cüzdan adı"
        placeholder="Yeni cüzdanın adı"
        value={name}
        onChange={(event) => setName(event.target.value)}
        required
      />
      <select aria-label="Para birimi" value={currency} onChange={(event) => setCurrency(event.target.value)}>
        {currencies.map((code) => (
          <option key={code}>{code}</option>
        ))}
      </select>
      <button type="submit" disabled={openWallet.isPending}>
        Cüzdan aç
      </button>
      {openWallet.error && <ErrorMessage error={openWallet.error} />}
    </form>
  )
}

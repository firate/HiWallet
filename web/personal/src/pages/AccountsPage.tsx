import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, kycLevel, money } from '../format'
import type { AccountDetailWallet, KycLevel } from '../types'

// Sistem hesapları para birimi başına ve bugün yalnızca TRY'de var; wallet-api başka
// bir para biriminde cüzdan açmıyor.
const walletCurrency = 'TRY'

/**
 * Hesaplar ve altındaki cüzdanlar. Hesabı kayıt açıyor; burada hesap açma yok.
 * Doğrulanmamış hesapta müşteri doğrulamaya yönleniyor.
 */
export function AccountsPage() {
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.accounts })

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
        <p>Bu kullanıcıya bağlı bir hesap yok.</p>
      </section>
    )
  }

  return (
    <>
      <h1>Cüzdanlarım</h1>
      {accounts.data.items.map((account) => (
        <AccountCard key={account.accountId} accountId={account.accountId} level={account.kycLevel} />
      ))}
    </>
  )
}

/** Doğrulama seviyesi ve müşterinin sonraki adımı. */
function LevelNotice({ level }: { level: KycLevel }) {
  if (level === 'Unknown') {
    return (
      <div className="notice">
        <p>Hesabın henüz doğrulanmadı. Para alıp gönderebilmek için telefonunu ve kimliğini doğrula.</p>
        <Link className="button" to="/dogrulama">
          Doğrulamayı tamamla
        </Link>
      </div>
    )
  }

  return <p className="muted small">Doğrulama seviyesi: {kycLevel(level)}</p>
}

function AccountCard({ accountId, level }: { accountId: string; level: KycLevel | null }) {
  const account = useQuery({ queryKey: ['accounts', accountId], queryFn: () => api.account(accountId) })

  if (account.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  const { wallets } = account.data

  return (
    <section className="card">
      <p>
        Hesap numaran: <strong className="account-number">{accountNumber(account.data.accountNumber)}</strong>
      </p>
      <p className="muted small">Sana para gönderecek kişiye bu numarayı ver.</p>
      {level !== null && <LevelNotice level={level} />}
      {level !== null && level !== 'Unknown' && (
        <p>
          <Link to={`/hesaplar/${accountId}/yukle`}>Havaleyle para yükle</Link>
        </p>
      )}
      {wallets.length === 0 ? (
        <p className="muted">Bu hesapta henüz cüzdan yok.</p>
      ) : (
        <ul className="wallets">
          {wallets.map((wallet) => (
            <li key={wallet.walletId}>
              <Link to={`/cuzdanlar/${wallet.walletId}`}>
                <span className="name">{wallet.name}</span>
                <span className="amount">{money(wallet.balance, wallet.currency)}</span>
              </Link>
              <DefaultMark
                accountId={accountId}
                wallet={wallet}
                choosable={wallets.filter((other) => other.currency === wallet.currency).length > 1}
              />
            </li>
          ))}
        </ul>
      )}
      <OpenWalletForm accountId={accountId} />
    </section>
  )
}

/**
 * Hesap numarasına gelen para para biriminin varsayılan cüzdanına düşüyor. Aynı para
 * biriminde birden fazla cüzdan varsa müşteri hangisi olacağını seçiyor.
 */
function DefaultMark({
  accountId,
  wallet,
  choosable,
}: {
  accountId: string
  wallet: AccountDetailWallet
  choosable: boolean
}) {
  const queryClient = useQueryClient()
  const makeDefault = useMutation({
    mutationFn: () => api.setDefaultWallet(accountId, wallet.currency, wallet.walletId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts', accountId] }),
  })

  if (wallet.isDefault) {
    return choosable ? <span className="muted small">Gelen para buraya</span> : null
  }

  if (!choosable) {
    return null
  }

  return (
    <>
      <button type="button" className="link" disabled={makeDefault.isPending} onClick={() => makeDefault.mutate()}>
        Gelen para buraya gelsin
      </button>
      {makeDefault.error && <ErrorMessage error={makeDefault.error} />}
    </>
  )
}

function OpenWalletForm({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const openWallet = useMutation({
    mutationFn: () => api.openWallet(accountId, name.trim(), walletCurrency),
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
      <span className="muted">{walletCurrency}</span>
      <button type="submit" disabled={openWallet.isPending}>
        Cüzdan aç
      </button>
      {openWallet.error && <ErrorMessage error={openWallet.error} />}
    </form>
  )
}

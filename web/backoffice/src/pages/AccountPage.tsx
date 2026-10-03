import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, accountType, date, kycLevel, money } from '../format'
import { useHasPermission } from '../session'
import type { AccountDetail } from '../types'

/** Müşterinin hesabı: tipi, seviyesi ve cüzdanları. */
export function AccountPage() {
  const { accountId = '' } = useParams()
  const account = useQuery({ queryKey: ['accounts', accountId], queryFn: () => api.account(accountId) })

  if (account.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  const { type, createdAt, wallets } = account.data

  return (
    <>
      <section className="card">
        <h1>{accountType(type)} hesap</h1>
        <dl>
          <dt>Hesap numarası</dt>
          <dd>
            <strong className="account-number">{accountNumber(account.data.accountNumber)}</strong>
          </dd>
          <dt>Kimlik</dt>
          <dd>
            <code>{accountId}</code>
          </dd>
          <dt>Açılış</dt>
          <dd>{date(createdAt)}</dd>
          {account.data.kycLevel && (
            <>
              <dt>Doğrulama seviyesi</dt>
              <dd>{kycLevel(account.data.kycLevel)}</dd>
            </>
          )}
          {type === 'Business' && (
            <>
              <dt>Platform promo'su</dt>
              <dd>{account.data.acceptsPromo ? 'Kabul ediyor' : 'Kabul etmiyor'}</dd>
            </>
          )}
        </dl>
        {type === 'Business' && <AcceptsPromoToggle account={account.data} />}
      </section>
      <section className="card">
        <h2>Cüzdanlar</h2>
        {wallets.length === 0 ? (
          <p className="muted">Cüzdanı yok.</p>
        ) : (
          <ul className="wallets">
            {wallets.map((wallet) => (
              <li key={wallet.walletId}>
                <Link to={`/cuzdanlar/${wallet.walletId}`}>
                  <span>
                    {wallet.name}
                    {wallet.isDefault && <span className="muted small"> varsayılan {wallet.currency}</span>}
                  </span>
                  <span>{money(wallet.balance, wallet.currency)}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  )
}

/**
 * İşyerinin platform fonlu promo kabulü. Bundan sonraki ödemeleri etkiliyor, verilmiş
 * partiler olduğu gibi kalıyor.
 */
function AcceptsPromoToggle({ account }: { account: AccountDetail }) {
  const allowed = useHasPermission('merchant.promo_acceptance')
  const queryClient = useQueryClient()
  const toggle = useMutation({
    mutationFn: () => api.setAcceptsPromo(account.accountId, !account.acceptsPromo),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts', account.accountId] }),
  })

  if (!allowed) {
    return null
  }

  return (
    <>
      <button type="button" className="secondary" onClick={() => toggle.mutate()} disabled={toggle.isPending}>
        {account.acceptsPromo ? 'Promo kabulünü kapat' : 'Promo kabulünü aç'}
      </button>
      {toggle.error && <ErrorMessage error={toggle.error} />}
    </>
  )
}

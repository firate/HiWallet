import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api, ApiError } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, accountType, consentDocument, date, day, kycLevel, kycMovement, money } from '../format'
import { useHasPermission } from '../session'
import type { AccountDetail, KycMovement } from '../types'

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
      {type === 'Person' && <Customer accountId={accountId} />}
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
      {account.data.kycLevel && (
        <Limits accountId={accountId} currency={wallets[0]?.currency ?? 'TRY'} />
      )}
    </>
  )
}

/**
 * Bireysel hesabın sahibi, onboarding'den. Kimlik numarası ve telefon maskeli geliyor;
 * numaranın ne zaman değiştiği hesabı ele geçirme şüphesinde ilk bakılan yer.
 */
function Customer({ accountId }: { accountId: string }) {
  const customer = useQuery({ queryKey: ['customers', accountId], queryFn: () => api.customer(accountId) })

  if (customer.isPending) {
    return null
  }

  if (customer.error) {
    // Hesap kayıttan açılmamış: onboarding'de sahibi yok.
    if (customer.error instanceof ApiError && customer.error.status === 404) {
      return (
        <section className="card">
          <h2>Müşteri</h2>
          <p className="muted">Hesabın kayıt bilgisi yok; kişisel bilgisi görüntülenemiyor.</p>
        </section>
      )
    }

    return <ErrorMessage error={customer.error} />
  }

  const profile = customer.data
  const name = [profile.firstName, profile.lastName].filter(Boolean).join(' ')

  return (
    <section className="card">
      <h2>Müşteri</h2>
      <dl>
        {name && (
          <>
            <dt>Ad soyad</dt>
            <dd>{name}</dd>
          </>
        )}
        <dt>E-posta</dt>
        <dd>{profile.email}</dd>
        {profile.nationalId && (
          <>
            <dt>T.C. kimlik no</dt>
            <dd>
              <code>{profile.nationalId}</code>
            </dd>
          </>
        )}
        {profile.birthDate && (
          <>
            <dt>Doğum tarihi</dt>
            <dd>{day(profile.birthDate)}</dd>
          </>
        )}
        <dt>Telefon</dt>
        <dd>
          {profile.phone && profile.phoneVerifiedAt ? (
            <>
              <code>{profile.phone}</code>
              <span className="muted small"> doğrulandı {date(profile.phoneVerifiedAt)}</span>
            </>
          ) : (
            'Doğrulanmadı.'
          )}
        </dd>
        <dt>Kimlik</dt>
        <dd>
          {profile.identityVerifiedAt
            ? `Nüfus kaydıyla eşleşti, ${date(profile.identityVerifiedAt)}`
            : 'Kimlik doğrulanmadı.'}
        </dd>
        <dt>Temel doğrulama</dt>
        <dd>{profile.basicVerifiedAt ? date(profile.basicVerifiedAt) : 'Tamamlanmadı.'}</dd>
      </dl>
      {profile.consents.length > 0 && (
        <>
          <h3>Onaylar</h3>
          <table>
            <thead>
              <tr>
                <th>Metin</th>
                <th>Sürüm</th>
                <th>Onay</th>
              </tr>
            </thead>
            <tbody>
              {profile.consents.map((consent) => (
                <tr key={`${consent.document}-${consent.version}`}>
                  <td>{consentDocument(consent.document)}</td>
                  <td>{consent.version}</td>
                  <td>{date(consent.acceptedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      {profile.phoneChanges.length > 0 && (
        <>
          <h3>Numara değişiklikleri</h3>
          <table>
            <thead>
              <tr>
                <th>Eski numara</th>
                <th>Yeni numara</th>
                <th>Tarih</th>
              </tr>
            </thead>
            <tbody>
              {profile.phoneChanges.map((change) => (
                <tr key={change.changedAt}>
                  <td>{change.oldPhone ?? '-'}</td>
                  <td>{change.newPhone}</td>
                  <td>{date(change.changedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  )
}

const order: KycMovement[] = ['OutgoingTransfer', 'Payment', 'Withdrawal', 'IncomingTransfer', 'Deposit', 'IncomingTotal']

/**
 * Seviyenin aylık limitleri ve bu ay kullanılanı: müşterinin "neden gönderemiyorum"
 * sorusunun cevabı. Sayılar wallet-api'nin limit kontrolünün saydığıyla aynı.
 */
function Limits({ accountId, currency }: { accountId: string; currency: string }) {
  const limits = useQuery({
    queryKey: ['accounts', accountId, 'limits', currency],
    queryFn: () => api.limits(accountId, currency),
  })

  if (limits.isPending) {
    return null
  }

  if (limits.error) {
    return <ErrorMessage error={limits.error} />
  }

  const { movements, balanceCap, balance, periodStart } = limits.data
  const unit = limits.data.currency

  return (
    <section className="card">
      <h2>Seviye limitleri</h2>
      <p className="muted small">Aylık. Kullanım {date(periodStart)} tarihinden beri sayılıyor.</p>
      <table>
        <thead>
          <tr>
            <th>Hareket</th>
            <th className="amount">Aylık limit</th>
            <th className="amount">Bu ay</th>
            <th className="amount">Kalan</th>
          </tr>
        </thead>
        <tbody>
          {order.map((name) => {
            const item = movements.find((candidate) => candidate.movement === name)

            if (!item) {
              return null
            }

            return (
              <tr key={name}>
                <td>{kycMovement(name)}</td>
                <td className="amount">{item.limit === 0 ? 'Kapalı' : money(item.limit, unit)}</td>
                <td className="amount">{money(item.used, unit)}</td>
                <td className="amount">{item.limit === 0 ? '-' : money(item.remaining, unit)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {balanceCap !== null && (
        <p className="muted">
          Bakiye tavanı {money(balanceCap, unit)}; şu an {money(balance, unit)}.
        </p>
      )}
    </section>
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

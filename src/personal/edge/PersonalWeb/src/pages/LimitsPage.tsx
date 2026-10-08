import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date, kycLevel, kycMovement, money } from '../format'
import { useLimits } from '../limits'
import type { KycMovement } from '../types'

/** Önce müşterinin kendi başlattığı hareketler, sonra hesaba gelenler. */
const order: KycMovement[] = ['OutgoingTransfer', 'Payment', 'Withdrawal', 'IncomingTransfer', 'Deposit', 'IncomingTotal']

/**
 * Seviyenin aylık limitleri ve bu ay kullanılanı. Sayılar sunucunun limit kontrolünün
 * saydığıyla aynı: burada kalan görünen tutar, işlemde de kalan tutar.
 */
export function LimitsPage() {
  const { accountId = '' } = useParams()
  const account = useQuery({ queryKey: ['accounts', accountId], queryFn: () => api.account(accountId) })
  const currency = account.data ? (account.data.wallets[0]?.currency ?? 'TRY') : undefined
  const limits = useLimits(accountId, account.data?.kycLevel, currency)

  const back = (
    <p>
      <Link to="/">Cüzdanlarım</Link>
    </p>
  )

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  if (account.data && account.data.kycLevel === null) {
    return (
      <>
        {back}
        <p>İşyeri hesabının seviye limiti yok.</p>
      </>
    )
  }

  if (limits.error) {
    return <ErrorMessage error={limits.error} />
  }

  if (!limits.data) {
    return <p className="muted">Yükleniyor...</p>
  }

  const { movements, balanceCap, balance, periodStart } = limits.data
  const unit = limits.data.currency

  return (
    <>
      {back}
      <section className="card">
        <h1>Limitlerim</h1>
        <p>
          Doğrulama seviyesi: <strong>{kycLevel(limits.data.kycLevel)}</strong>
        </p>
        <p className="muted small">Aylık limitler. Kullanım {date(periodStart)} tarihinden beri sayılıyor.</p>
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
            Bakiye tavanı {money(balanceCap, unit)}: bütün cüzdanlarının toplamı bunu aşamaz. Şu an{' '}
            {money(balance, unit)}.
          </p>
        )}
      </section>
    </>
  )
}

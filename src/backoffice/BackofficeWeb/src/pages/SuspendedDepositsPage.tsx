import { useInfiniteQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, date, depositHoldReason, money } from '../format'

/**
 * Cüzdana geçirilemeyip askıya alınan havaleler, yeniden eskiye. Para bankamızda ve
 * askı hesabında. Gönderenin adı ve IBAN'ı banka entegrasyonunda; burada banka
 * referansı, sebep ve açıklamadaki numaranın hesabı.
 */
export function SuspendedDepositsPage() {
  const deposits = useInfiniteQuery({
    queryKey: ['suspended-deposits'],
    queryFn: ({ pageParam }) => api.suspendedDeposits(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  const items = deposits.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <section className="card">
      <h1>Askıdaki havaleler</h1>
      {deposits.isPending && <p className="muted">Yükleniyor...</p>}
      {deposits.error && <ErrorMessage error={deposits.error} />}
      {deposits.isSuccess && items.length === 0 && <p className="muted">Askıda havale yok.</p>}
      {items.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Geldi</th>
              <th>Banka referansı</th>
              <th>Sebep</th>
              <th>Hesap</th>
              <th className="amount">Tutar</th>
            </tr>
          </thead>
          <tbody>
            {items.map((deposit) => (
              <tr key={deposit.id}>
                <td>{date(deposit.receivedAt)}</td>
                <td>{deposit.bankReference}</td>
                <td>{depositHoldReason(deposit.reason)}</td>
                <td>
                  {deposit.accountId && deposit.accountNumber ? (
                    <Link to={`/hesaplar/${deposit.accountId}`}>{accountNumber(deposit.accountNumber)}</Link>
                  ) : (
                    <span className="muted">yok</span>
                  )}
                </td>
                <td className="amount">{money(deposit.amount, deposit.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {deposits.hasNextPage && (
        <button className="secondary" onClick={() => deposits.fetchNextPage()} disabled={deposits.isFetchingNextPage}>
          Devamı
        </button>
      )}
    </section>
  )
}

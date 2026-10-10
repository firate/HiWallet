import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Fragment, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, date, depositHoldReason, money } from '../format'
import { useIdempotencyKey } from '../idempotency'
import { useHasPermission } from '../session'
import type { SuspendedDeposit } from '../types'

/**
 * Cüzdana geçirilemeyip askıya alınan havaleler, yeniden eskiye. Para bankamızda ve
 * askı hesabında. Gönderenin adı ve IBAN'ı banka entegrasyonunda; burada banka
 * referansı, sebep ve açıklamadaki numaranın hesabı. İzni olan çalışan havaleyi bir
 * hesabın cüzdanına aktarıyor; aktarılan havale listeden çıkıyor.
 */
export function SuspendedDepositsPage() {
  const canMove = useHasPermission('deposit.resolve')
  const [moving, setMoving] = useState<string | null>(null)
  const [moved, setMoved] = useState<string | null>(null)
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
      {moved && (
        <p className="success" role="status">
          Havale {accountNumber(moved)} hesabının cüzdanına aktarıldı.
        </p>
      )}
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
              {canMove && <th />}
            </tr>
          </thead>
          <tbody>
            {items.map((deposit) => (
              <Fragment key={deposit.id}>
                <tr>
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
                  {canMove && (
                    <td>
                      <button
                        type="button"
                        className="secondary"
                        onClick={() => {
                          setMoving(moving === deposit.id ? null : deposit.id)
                          setMoved(null)
                        }}
                      >
                        Cüzdana aktar
                      </button>
                    </td>
                  )}
                </tr>
                {moving === deposit.id && (
                  <tr>
                    <td colSpan={6}>
                      <MoveForm
                        deposit={deposit}
                        onMoved={(number) => {
                          setMoving(null)
                          setMoved(number)
                        }}
                      />
                    </td>
                  </tr>
                )}
              </Fragment>
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

/**
 * Havaleyi bir hesabın varsayılan cüzdanına aktarma. Açıklamadaki numara önerilen hesap;
 * çalışan paranın sahibini başka yoldan biliyorsa değiştiriyor. Hesabın bireysel olması,
 * cüzdanı ve seviye limiti wallet-api'de kontrol ediliyor; limiti aşan havale yalnızca
 * kaynağına iade edilebilir.
 */
function MoveForm({ deposit, onMoved }: { deposit: SuspendedDeposit; onMoved: (accountNumber: string) => void }) {
  const queryClient = useQueryClient()
  const [number, setNumber] = useState(deposit.accountNumber ?? '')
  const [key, renewKey] = useIdempotencyKey()
  const move = useMutation({
    mutationFn: (target: string) => api.moveSuspendedDeposit(deposit.id, target, key),
    onSuccess: async (_, target) => {
      renewKey()
      onMoved(target)
      await queryClient.invalidateQueries({ queryKey: ['suspended-deposits'] })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    move.mutate(number.replaceAll(' ', ''))
  }

  return (
    <>
      <form className="inline" onSubmit={submit}>
        <input
          aria-label="Hesap numarası"
          className="grow"
          placeholder="Hesap numarası"
          value={number}
          onChange={(event) => setNumber(event.target.value)}
        />
        <button type="submit" disabled={move.isPending}>
          Aktar
        </button>
      </form>
      {move.error && <ErrorMessage error={move.error} />}
    </>
  )
}

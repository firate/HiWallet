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
 * hesabın cüzdanına aktarıyor ya da göndericiye iade ediyor; aktarılan ve iadesi tamamlanan
 * havale listeden çıkıyor, iadesi süren listede kalıyor.
 */
export function SuspendedDepositsPage() {
  const canResolve = useHasPermission('deposit.resolve')
  const [open, setOpen] = useState<{ id: string; action: 'move' | 'return' } | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const deposits = useInfiniteQuery({
    queryKey: ['suspended-deposits'],
    queryFn: ({ pageParam }) => api.suspendedDeposits(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  const items = deposits.data?.pages.flatMap((page) => page.items) ?? []

  function toggle(id: string, action: 'move' | 'return') {
    setOpen(open?.id === id && open.action === action ? null : { id, action })
    setNotice(null)
  }

  function done(message: string) {
    setOpen(null)
    setNotice(message)
  }

  return (
    <section className="card">
      <h1>Askıdaki havaleler</h1>
      {notice && (
        <p className="success" role="status">
          {notice}
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
              <th />
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
                  <td>
                    {deposit.status === 'returning' ? (
                      <span className="muted">İade ediliyor</span>
                    ) : (
                      canResolve && (
                        <span className="actions">
                          <button type="button" className="secondary" onClick={() => toggle(deposit.id, 'move')}>
                            Cüzdana aktar
                          </button>
                          <button type="button" className="secondary" onClick={() => toggle(deposit.id, 'return')}>
                            İade et
                          </button>
                        </span>
                      )
                    )}
                  </td>
                </tr>
                {open?.id === deposit.id && (
                  <tr>
                    <td colSpan={6}>
                      {open.action === 'move' ? (
                        <MoveForm
                          deposit={deposit}
                          onMoved={(number) => done(`Havale ${accountNumber(number)} hesabının cüzdanına aktarıldı.`)}
                        />
                      ) : (
                        <ReturnForm deposit={deposit} onStarted={() => done('İade başladı; bankanın sonucu bekleniyor.')} />
                      )}
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

/**
 * Havaleyi göndericiye iade. IBAN panelde yok: banka entegrasyonu havalenin kaydından okuyor.
 * Para hemen gitmiyor; askıdan düşülüyor ve bankanın sonucu bekleniyor. Banka reddederse
 * havale askıya döner ve yeniden karara açılır.
 */
function ReturnForm({ deposit, onStarted }: { deposit: SuspendedDeposit; onStarted: () => void }) {
  const queryClient = useQueryClient()
  const [key, renewKey] = useIdempotencyKey()
  const start = useMutation({
    mutationFn: () => api.startDepositReturn(deposit.id, key),
    onSuccess: async () => {
      renewKey()
      onStarted()
      await queryClient.invalidateQueries({ queryKey: ['suspended-deposits'] })
    },
  })

  return (
    <>
      <p>
        {money(deposit.amount, deposit.currency)} gönderenin IBAN'ına iade edilecek. Banka ücretini platform
        yükleniyor.
      </p>
      <button type="button" onClick={() => start.mutate()} disabled={start.isPending}>
        İadeyi başlat
      </button>
      {start.error && <ErrorMessage error={start.error} />}
    </>
  )
}

import { useInfiniteQuery } from '@tanstack/react-query'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import { auditAction, date } from '../../format'
import { StaffSection } from './StaffSection'

/** Personel yönetimindeki değişikliklerin kaydı; değiştirilemiyor ve silinemiyor. */
export function AuditPage() {
  const events = useInfiniteQuery({
    queryKey: ['audit-events'],
    queryFn: ({ pageParam }) => api.auditEvents(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })

  const items = events.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <StaffSection>
      <section className="card">
        <h1>Kayıtlar</h1>
        {events.isPending && <p className="muted">Yükleniyor...</p>}
        {events.error && <ErrorMessage error={events.error} />}
        {events.isSuccess && items.length === 0 && <p className="muted">Kayıt yok.</p>}
        {items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Zaman</th>
                <th>Yapan</th>
                <th>İşlem</th>
                <th>Kime</th>
                <th>Ayrıntı</th>
              </tr>
            </thead>
            <tbody>
              {items.map((event) => (
                <tr key={event.eventId}>
                  <td className="small">{date(event.occurredAt)}</td>
                  <td>{event.actorName ?? event.actorSubject}</td>
                  <td>{auditAction(event.action)}</td>
                  <td>{event.targetLabel}</td>
                  <td className="small">{summary(event.details)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {events.hasNextPage && (
          <button className="secondary" onClick={() => events.fetchNextPage()} disabled={events.isFetchingNextPage}>
            Daha eski kayıtlar
          </button>
        )}
      </section>
    </StaffSection>
  )
}

/** Ayrıntının okunabilir hali: önce ve sonra, ya da verilen izinler ve roller. */
function summary(details: Record<string, unknown>): string {
  const names = (value: unknown): string | null => {
    if (Array.isArray(value)) return value.length === 0 ? 'yok' : value.join(', ')
    if (value && typeof value === 'object' && 'permissions' in value) return names(value.permissions)
    return null
  }

  const before = names(details.before)
  const after = names(details.after)

  if (before !== null && after !== null) {
    return `önce: ${before}; sonra: ${after}`
  }

  return names(details.permissions) ?? names(details.roles) ?? ''
}

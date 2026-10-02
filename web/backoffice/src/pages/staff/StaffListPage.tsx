import { useInfiniteQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import { personName, staffStatus } from '../../format'
import { StaffSection } from './StaffSection'

export function StaffListPage() {
  const [draft, setDraft] = useState('')
  const [search, setSearch] = useState('')

  const staff = useInfiniteQuery({
    queryKey: ['staff', 'list', search],
    queryFn: ({ pageParam }) => api.staffList(pageParam, search),
    initialPageParam: null as number | null,
    getNextPageParam: (last) => last.nextFirst,
  })

  const items = staff.data?.pages.flatMap((page) => page.items) ?? []

  function submit(event: FormEvent) {
    event.preventDefault()
    setSearch(draft.trim())
  }

  return (
    <StaffSection>
      <section className="card">
        <div className="heading">
          <h1>Çalışanlar</h1>
          <Link className="button" to="/personel/yeni">
            Çalışan davet et
          </Link>
        </div>
        <form className="inline" onSubmit={submit}>
          <input
            aria-label="Ara"
            className="grow"
            placeholder="E-posta, ad ya da soyad"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
          />
          <button type="submit" className="secondary">
            Ara
          </button>
        </form>
        {staff.isPending && <p className="muted">Yükleniyor...</p>}
        {staff.error && <ErrorMessage error={staff.error} />}
        {staff.isSuccess && items.length === 0 && <p className="muted">Çalışan yok.</p>}
        {items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Çalışan</th>
                <th>E-posta</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
              {items.map((member) => (
                <tr key={member.staffId}>
                  <td>
                    <Link to={`/personel/${member.staffId}`}>{personName(member)}</Link>
                  </td>
                  <td>{member.email}</td>
                  <td>{staffStatus(member)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {staff.hasNextPage && (
          <button className="secondary" onClick={() => staff.fetchNextPage()} disabled={staff.isFetchingNextPage}>
            Devamı
          </button>
        )}
      </section>
    </StaffSection>
  )
}

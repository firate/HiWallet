import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import { date, personName, staffStatus } from '../../format'
import { useSessionUser } from '../../session'
import type { StaffDetail } from '../../types'
import { RoleChecklist } from './RoleChecklist'
import { StaffSection } from './StaffSection'

/**
 * Çalışanın durumu ve rolleri. Kendi hesabında rol değiştirme ve kapatma yok: kendine
 * yetki verememeli. Kuralı iç servis de uyguluyor.
 */
export function StaffPage() {
  const { staffId = '' } = useParams()
  const user = useSessionUser()
  const staff = useQuery({ queryKey: ['staff', staffId], queryFn: () => api.staff(staffId) })

  return (
    <StaffSection>
      <p>
        <Link to="/personel">Çalışanlar</Link>
      </p>
      {staff.isPending && <p className="muted">Yükleniyor...</p>}
      {staff.error && <ErrorMessage error={staff.error} />}
      {staff.data && <StaffCard staff={staff.data} self={staff.data.staffId === user.subject} />}
    </StaffSection>
  )
}

function StaffCard({ staff, self }: { staff: StaffDetail; self: boolean }) {
  const queryClient = useQueryClient()
  const [notice, setNotice] = useState<string | null>(null)

  async function show(updated: StaffDetail, message: string) {
    queryClient.setQueryData(['staff', staff.staffId], updated)
    await queryClient.invalidateQueries({ queryKey: ['staff', 'list'] })
    setNotice(message)
  }

  const setEnabled = useMutation({
    mutationFn: (enabled: boolean) => (enabled ? api.enableStaff(staff.staffId) : api.disableStaff(staff.staffId)),
    onSuccess: (updated) => show(updated, updated.enabled ? 'Çalışan açıldı.' : 'Çalışan kapatıldı; oturumları da kapandı.'),
  })

  const resend = useMutation({
    mutationFn: () => api.resendInvitation(staff.staffId),
    onSuccess: () => setNotice('Davet yeniden gönderildi.'),
  })

  return (
    <>
      <section className="card">
        <h1>{personName(staff)}</h1>
        <dl>
          <dt>E-posta</dt>
          <dd>{staff.email}</dd>
          <dt>Durum</dt>
          <dd>{staffStatus(staff)}</dd>
          <dt>Açılış</dt>
          <dd>{date(staff.createdAt)}</dd>
        </dl>
        {self ? (
          <p className="muted">Kendi hesabın: rollerini ve durumunu başka bir yönetici değiştirebilir.</p>
        ) : (
          <div className="actions">
            <button
              type="button"
              className="secondary"
              onClick={() => setEnabled.mutate(!staff.enabled)}
              disabled={setEnabled.isPending}
            >
              {staff.enabled ? 'Kapat' : 'Aç'}
            </button>
            {staff.invitationPending && (
              <button type="button" className="secondary" onClick={() => resend.mutate()} disabled={resend.isPending}>
                Daveti yeniden gönder
              </button>
            )}
          </div>
        )}
        {notice && (
          <div className="success">
            <p>{notice}</p>
          </div>
        )}
        {setEnabled.error && <ErrorMessage error={setEnabled.error} />}
        {resend.error && <ErrorMessage error={resend.error} />}
      </section>
      {self ? (
        <section className="card">
          <h2>Roller</h2>
          {staff.roles.length === 0 ? (
            <p className="muted">Rolü yok.</p>
          ) : (
            <ul className="links">
              {staff.roles.map((role) => (
                <li key={role.roleId}>{role.name}</li>
              ))}
            </ul>
          )}
        </section>
      ) : (
        <RolesCard staff={staff} onSaved={(updated) => show(updated, 'Roller kaydedildi.')} />
      )}
    </>
  )
}

function RolesCard({ staff, onSaved }: { staff: StaffDetail; onSaved: (updated: StaffDetail) => void }) {
  const [roleIds, setRoleIds] = useState(() => staff.roles.map((role) => role.roleId))
  const save = useMutation({ mutationFn: () => api.setStaffRoles(staff.staffId, roleIds), onSuccess: onSaved })

  return (
    <section className="card">
      <h2>Roller</h2>
      <RoleChecklist selected={roleIds} onChange={setRoleIds} />
      <button type="button" onClick={() => save.mutate()} disabled={save.isPending}>
        Rolleri kaydet
      </button>
      {save.error && <ErrorMessage error={save.error} />}
    </section>
  )
}

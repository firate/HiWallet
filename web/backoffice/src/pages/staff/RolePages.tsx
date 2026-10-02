import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import { personName, staffStatus } from '../../format'
import type { RoleDetail, StaffPermission } from '../../types'
import { inCodeOrder, PermissionChecklist } from './RoleChecklist'
import { StaffSection } from './StaffSection'

/** Panelin rolleri: kodun izinlerinden kurulan setler. */
export function RolesPage() {
  const roles = useQuery({ queryKey: ['roles', 'list'], queryFn: () => api.roles() })

  return (
    <StaffSection>
      <section className="card">
        <div className="heading">
          <h1>Roller</h1>
          <Link className="button" to="/roller/yeni">
            Yeni rol
          </Link>
        </div>
        <p className="muted small">
          Rol bir izin setidir. Rolün içeriği değişince o roldeki herkesin yetkisi birlikte değişir.
        </p>
        {roles.isPending && <p className="muted">Yükleniyor...</p>}
        {roles.error && <ErrorMessage error={roles.error} />}
        {roles.data && roles.data.items.length === 0 && <p className="muted">Rol yok.</p>}
        {roles.data && roles.data.items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Rol</th>
                <th>İzinler</th>
              </tr>
            </thead>
            <tbody>
              {roles.data.items.map((role) => (
                <tr key={role.roleId}>
                  <td>
                    <Link to={`/roller/${role.roleId}`}>{role.name}</Link>
                    {role.description && <div className="muted small">{role.description}</div>}
                  </td>
                  <td className="small">{role.permissions.join(', ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </StaffSection>
  )
}

/** Rolün adı sonradan değişmiyor: token'a ve kayda bu adla yazılıyor. */
export function NewRolePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: api.permissions, staleTime: Infinity })
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [selected, setSelected] = useState<StaffPermission[]>([])

  const create = useMutation({
    mutationFn: () =>
      api.createRole(name.trim(), {
        description: description.trim() || null,
        permissions: inCodeOrder(selected, permissions.data),
      }),
    onSuccess: async (role) => {
      await queryClient.invalidateQueries({ queryKey: ['roles', 'list'] })
      navigate(`/roller/${role.roleId}`)
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    create.mutate()
  }

  return (
    <StaffSection>
      <section className="card">
        <p>
          <Link to="/roller">Roller</Link>
        </p>
        <h1>Yeni rol</h1>
        <form className="stacked" onSubmit={submit}>
          <label>
            Ad
            <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={64} />
          </label>
          <label>
            Açıklama
            <input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={200} />
          </label>
          <PermissionChecklist selected={selected} onChange={setSelected} />
          <button type="submit" disabled={create.isPending || selected.length === 0}>
            Rolü aç
          </button>
        </form>
        {create.error && <ErrorMessage error={create.error} />}
      </section>
    </StaffSection>
  )
}

/** Rolün izinleri ve üyeleri. Sahip olduğun rolü değiştiremez ve silemezsin; reddi iç servis veriyor. */
export function RolePage() {
  const { roleId = '' } = useParams()
  const role = useQuery({ queryKey: ['roles', roleId], queryFn: () => api.role(roleId) })

  return (
    <StaffSection>
      <p>
        <Link to="/roller">Roller</Link>
      </p>
      {role.isPending && <p className="muted">Yükleniyor...</p>}
      {role.error && <ErrorMessage error={role.error} />}
      {role.data && <RoleEditor role={role.data} />}
    </StaffSection>
  )
}

function RoleEditor({ role }: { role: RoleDetail }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: api.permissions, staleTime: Infinity })
  const [description, setDescription] = useState(role.description ?? '')
  const [selected, setSelected] = useState<StaffPermission[]>(role.permissions)
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () =>
      api.updateRole(role.roleId, {
        description: description.trim() || null,
        permissions: inCodeOrder(selected, permissions.data),
      }),
    onSuccess: async () => {
      setSaved(true)
      await queryClient.invalidateQueries({ queryKey: ['roles'] })
    },
  })

  const remove = useMutation({
    mutationFn: () => api.deleteRole(role.roleId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['roles'] })
      navigate('/roller')
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setSaved(false)
    save.mutate()
  }

  function confirmRemove() {
    const who = role.members.length === 0 ? 'Rolde kimse yok.' : `${role.members.length} çalışandan alınacak.`
    if (window.confirm(`${role.name} rolü silinsin mi? ${who}`)) {
      remove.mutate()
    }
  }

  return (
    <>
      <section className="card">
        <h1>{role.name}</h1>
        <form className="stacked" onSubmit={submit}>
          <label>
            Açıklama
            <input value={description} onChange={(event) => setDescription(event.target.value)} maxLength={200} />
          </label>
          <PermissionChecklist selected={selected} onChange={setSelected} />
          <div className="actions">
            <button type="submit" disabled={save.isPending || selected.length === 0}>
              Kaydet
            </button>
            <button type="button" className="secondary" onClick={confirmRemove} disabled={remove.isPending}>
              Rolü sil
            </button>
          </div>
        </form>
        {saved && (
          <div className="success">
            <p>Rol kaydedildi.</p>
          </div>
        )}
        {save.error && <ErrorMessage error={save.error} />}
        {remove.error && <ErrorMessage error={remove.error} />}
      </section>
      <section className="card">
        <h2>Bu roldeki çalışanlar</h2>
        {role.members.length === 0 ? (
          <p className="muted">Kimse yok.</p>
        ) : (
          <table>
            <tbody>
              {role.members.map((member) => (
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
      </section>
    </>
  )
}

import { useQuery } from '@tanstack/react-query'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import type { Permission, StaffPermission } from '../../types'

/** Panelin rolleri, işaretlenerek seçiliyor; her rolün altında içerdiği izinler. */
export function RoleChecklist({
  selected,
  onChange,
}: {
  selected: string[]
  onChange: (roleIds: string[]) => void
}) {
  const roles = useQuery({ queryKey: ['roles', 'list'], queryFn: () => api.roles() })

  if (roles.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (roles.error) {
    return <ErrorMessage error={roles.error} />
  }

  if (roles.data.items.length === 0) {
    return <p className="muted">Henüz rol yok; önce Roller'den bir rol aç.</p>
  }

  function toggle(roleId: string, checked: boolean) {
    onChange(checked ? [...selected, roleId] : selected.filter((id) => id !== roleId))
  }

  return (
    <fieldset className="checklist">
      <legend>Roller</legend>
      {roles.data.items.map((role) => (
        <label key={role.roleId} className="check">
          <input
            type="checkbox"
            checked={selected.includes(role.roleId)}
            onChange={(event) => toggle(role.roleId, event.target.checked)}
          />
          <span>
            {role.name}
            <span className="muted small"> {role.permissions.join(', ')}</span>
          </span>
        </label>
      ))}
    </fieldset>
  )
}

/** Kodun izinleri, açıklamalarıyla; rol bunlardan kuruluyor. */
export function PermissionChecklist({
  selected,
  onChange,
}: {
  selected: StaffPermission[]
  onChange: (permissions: StaffPermission[]) => void
}) {
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: api.permissions, staleTime: Infinity })

  if (permissions.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (permissions.error) {
    return <ErrorMessage error={permissions.error} />
  }

  function toggle(permission: Permission, checked: boolean) {
    onChange(checked ? [...selected, permission.name] : selected.filter((name) => name !== permission.name))
  }

  return (
    <fieldset className="checklist">
      <legend>İzinler</legend>
      {permissions.data.map((permission) => (
        <label key={permission.name} className="check">
          <input
            type="checkbox"
            checked={selected.includes(permission.name)}
            onChange={(event) => toggle(permission, event.target.checked)}
          />
          <span>
            {permission.description}
            <span className="muted small"> {permission.name}</span>
          </span>
        </label>
      ))}
    </fieldset>
  )
}

/** İzinler kodun sırasıyla gönderiliyor; işaretlenme sırası değil. */
export function inCodeOrder(selected: StaffPermission[], all: Permission[] | undefined): StaffPermission[] {
  return all ? all.map((p) => p.name).filter((name) => selected.includes(name)) : selected
}

import { Link, NavLink, Outlet } from 'react-router'
import { staffRoleNames } from '../format'
import { useHasPermission, useSessionUser } from '../session'

export function Layout() {
  const user = useSessionUser()
  const managesStaff = useHasPermission('staff.manage')

  return (
    <>
      <header className="top">
        <div className="nav">
          <Link to="/" className="brand">
            HiWallet <span className="muted">Backoffice</span>
          </Link>
          <NavLink to="/cekimler">Çekimler</NavLink>
          <NavLink to="/kampanyalar">Kampanyalar</NavLink>
          {managesStaff && <NavLink to="/personel">Personel</NavLink>}
        </div>
        <div className="user">
          <span>{user.name ?? user.email ?? user.subject}</span>
          <span className="muted small">{staffRoleNames(user.roles).join(', ')}</span>
          {/* Çıkış sayfa geçişi: BFF cookie'yi siliyor ve Keycloak'taki oturumu da kapatıyor. */}
          <form method="post" action="/bff/logout">
            <button type="submit" className="link">
              Çıkış
            </button>
          </form>
        </div>
      </header>
      <main>
        <Outlet />
      </main>
    </>
  )
}

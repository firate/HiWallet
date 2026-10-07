import { Link, NavLink, Outlet } from 'react-router'
import { useHasPermission, useSessionUser, useStaffAccess } from '../session'

export function Layout() {
  const user = useSessionUser()
  const access = useStaffAccess()
  // Menü yalnızca izni olan sayfaları gösteriyor; yetkiyi yine iç servis kontrol ediyor.
  const viewsCustomers = useHasPermission('customer.view')
  const viewsCampaigns = useHasPermission('campaign.view')
  const managesStaff = useHasPermission('staff.manage')
  const viewsDeposits = useHasPermission('deposit.view')

  return (
    <>
      <header className="top">
        <div className="nav">
          <Link to="/" className="brand">
            HiWallet <span className="muted">Backoffice</span>
          </Link>
          {viewsCustomers && <NavLink to="/cekimler">Çekimler</NavLink>}
          {viewsCampaigns && <NavLink to="/kampanyalar">Kampanyalar</NavLink>}
          {viewsDeposits && <NavLink to="/havaleler">Askıdaki havaleler</NavLink>}
          {managesStaff && <NavLink to="/personel">Personel</NavLink>}
        </div>
        <div className="user">
          <span>{user.name ?? user.email ?? user.subject}</span>
          <span className="muted small">{access.roles.join(', ')}</span>
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

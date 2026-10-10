import { Link, Outlet } from 'react-router'
import { useSessionUser } from '../session'

export function Layout() {
  const user = useSessionUser()

  return (
    <>
      <header className="top">
        <Link to="/" className="brand">
          HiWallet
        </Link>
        <div className="user">
          <Link to="/profil">{user.name ?? user.email ?? user.subject}</Link>
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

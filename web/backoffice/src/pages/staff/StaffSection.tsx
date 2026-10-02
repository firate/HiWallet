import type { ReactNode } from 'react'
import { NavLink } from 'react-router'
import { useHasPermission } from '../../session'

/**
 * Personel yönetiminin sayfaları: çalışanlar, roller, kayıt. İzni olmayan çalışan menüde
 * görmüyor; adresi yazsa da yalnızca açıklama görüyor ve istekleri iç servis reddediyor.
 */
export function StaffSection({ children }: { children: ReactNode }) {
  const allowed = useHasPermission('staff.manage')

  if (!allowed) {
    return (
      <section className="card">
        <p>Personel yönetimi için personel yönetimi izni gerekiyor.</p>
      </section>
    )
  }

  return (
    <>
      <nav className="tabs">
        <NavLink to="/personel" end>
          Çalışanlar
        </NavLink>
        <NavLink to="/roller">Roller</NavLink>
        <NavLink to="/personel/kayitlar">Kayıtlar</NavLink>
      </nav>
      {children}
    </>
  )
}

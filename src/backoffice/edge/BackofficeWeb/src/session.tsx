import { useQuery } from '@tanstack/react-query'
import { createContext, use, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { api, loginUrl, SessionExpiredError } from './api'
import { ErrorMessage } from './components/ErrorMessage'
import type { SessionUser, StaffAccess, StaffPermission } from './types'

export const sessionQueryKey = ['session'] as const

/**
 * Çalışanın şu anki izinleri. Pencereye dönünce ve bir istek 403 alınca yeniden
 * soruluyor (queryClient): rolü alınan çalışanın menüsü de değişiyor.
 */
export const accessQueryKey = ['access'] as const

interface Session {
  user: SessionUser
  access: StaffAccess
}

const SessionContext = createContext<Session | null>(null)

function useSession(): Session {
  const session = use(SessionContext)

  if (session === null) {
    throw new Error('Oturum SessionGate dışında okundu.')
  }

  return session
}

export function useSessionUser(): SessionUser {
  return useSession().user
}

export function useStaffAccess(): StaffAccess {
  return useSession().access
}

/**
 * Düğmeyi göstermek için; yetkiyi iç servis her istekte kendisi kontrol ediyor. İzni
 * olmayan çalışan düğmeye ulaşsa da istek reddediliyor.
 */
export function useHasPermission(permission: StaffPermission): boolean {
  return useSession().access.permissions.includes(permission)
}

/**
 * Oturum açıksa paneli, değilse girişi gösteriyor. Hiçbir izni olmayan çalışana (rolü
 * yok ya da kapatılmış) panel yalnızca rolü olmadığını söylüyor.
 */
export function SessionGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: sessionQueryKey, queryFn: api.user, retry: false })
  const access = useQuery({ queryKey: accessQueryKey, queryFn: api.access, enabled: session.isSuccess })

  if (session.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (session.error instanceof SessionExpiredError) {
    return <SignIn />
  }

  if (session.error) {
    return <ErrorMessage error={session.error} />
  }

  // Yenileme hata alırsa panel son bilinen izinlerle kalıyor; iç servis yine reddediyor.
  if (access.data === undefined) {
    return access.error ? <ErrorMessage error={access.error} /> : <p className="muted">Yükleniyor...</p>
  }

  if (access.data.permissions.length === 0) {
    return <NoRole />
  }

  return <SessionContext value={{ user: session.data, access: access.data }}>{children}</SessionContext>
}

function SignIn() {
  const location = useLocation()

  return (
    <section className="card sign-in">
      <h1>HiWallet Backoffice</h1>
      <p>Çalışan girişi. Girişte doğrulayıcı uygulamandaki kod da soruluyor.</p>
      <a className="button" href={loginUrl(location.pathname + location.search)}>
        Giriş yap
      </a>
    </section>
  )
}

function NoRole() {
  return (
    <section className="card sign-in">
      <h1>HiWallet Backoffice</h1>
      <p>Kullanıcına henüz rol atanmamış. Yöneticinden seni bir ekibe eklemesini iste.</p>
      {/* Başka bir kullanıcıyla girmek için. Çıkış sayfa geçişi: Keycloak'taki oturum da kapanıyor. */}
      <form method="post" action="/bff/logout">
        <button type="submit" className="secondary">
          Çıkış
        </button>
      </form>
    </section>
  )
}

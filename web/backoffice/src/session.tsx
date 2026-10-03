import { useQuery } from '@tanstack/react-query'
import { createContext, use, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { ApiError, api, loginUrl, SessionExpiredError } from './api'
import { ErrorMessage } from './components/ErrorMessage'
import type { SessionUser, StaffRole } from './types'

export const sessionQueryKey = ['session'] as const

const SessionContext = createContext<SessionUser | null>(null)

export function useSessionUser(): SessionUser {
  const user = use(SessionContext)

  if (user === null) {
    throw new Error('useSessionUser SessionGate dışında kullanıldı.')
  }

  return user
}

/**
 * Düğmeyi göstermek için; yetkiyi iç servis kendisi kontrol ediyor. Rolü olmayan
 * çalışan düğmeye ulaşsa da istek reddediliyor.
 */
export function useHasRole(role: StaffRole): boolean {
  return useSessionUser().roles.includes(role)
}

/**
 * Oturum açıksa paneli, değilse girişi gösteriyor. BFF rolü olmayan çalışanın
 * kullanıcısını da vermiyor (403); panel ona yalnızca rolü olmadığını söylüyor.
 */
export function SessionGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: sessionQueryKey, queryFn: api.user, retry: false })

  if (session.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (session.error instanceof SessionExpiredError) {
    return <SignIn />
  }

  if (session.error instanceof ApiError && session.error.status === 403) {
    return <NoRole />
  }

  if (session.error) {
    return <ErrorMessage error={session.error} />
  }

  return <SessionContext value={session.data}>{children}</SessionContext>
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

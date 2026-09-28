import { useQuery } from '@tanstack/react-query'
import { createContext, use, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { api, loginUrl, SessionExpiredError } from './api'
import { ErrorMessage } from './components/ErrorMessage'
import type { SessionUser } from './types'

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
 * Oturum açıksa uygulamayı, değilse girişi gösteriyor. Oturumun durumunu BFF biliyor:
 * cookie HttpOnly, uygulama onu göremiyor ve kullanıcıyı BFF'e soruyor.
 */
export function SessionGate({ children }: { children: ReactNode }) {
  const session = useQuery({ queryKey: sessionQueryKey, queryFn: api.user, retry: false })

  if (session.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (session.error instanceof SessionExpiredError) {
    return <SignIn />
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
      <h1>HiWallet</h1>
      <p>Cüzdanlarını görmek ve para göndermek için giriş yap.</p>
      <a className="button" href={loginUrl(location.pathname + location.search)}>
        Giriş yap
      </a>
      <p className="muted small">
        Hesabın yok mu? <Link to="/kayit">Kayıt ol</Link>
      </p>
    </section>
  )
}

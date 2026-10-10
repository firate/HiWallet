import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { date } from '../format'
import { needsReauthentication, reauthenticate } from '../reauthentication'
import type { PhoneChanged, PhoneVerificationStarted } from '../types'

const statusKey = ['onboarding'] as const

/** Yeniden girişe gidip dönerken yazılan numara kaybolmasın; yalnızca bu sekmede. */
const pendingPhoneKey = 'hiwallet.telefon-degisikligi'

/** Dönüş adresindeki işaret: müşteri parolasıyla yeni girdi, ikinci kez yönlendirilmiyor. */
const returnedPath = '/profil?yeniden=1'

/** Müşterinin bilgileri ve telefon değiştirme. */
export function ProfilePage() {
  const status = useQuery({ queryKey: statusKey, queryFn: api.onboardingStatus })

  if (status.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (status.error) {
    return <ErrorMessage error={status.error} />
  }

  const { email, phone, basicVerificationCompleted } = status.data

  return (
    <>
      <p>
        <Link to="/">Cüzdanlarım</Link>
      </p>
      <section className="card">
        <h1>Profil</h1>
        <dl>
          <dt>E-posta</dt>
          <dd>{email ?? '-'}</dd>
          <dt>Telefon</dt>
          <dd>{phone ?? '-'}</dd>
        </dl>
      </section>
      {basicVerificationCompleted ? (
        <PhoneChange />
      ) : (
        <section className="card">
          <p>
            Numaran doğrulamanın telefon adımında değişiyor. <Link to="/dogrulama">Doğrulamayı tamamla</Link>
          </p>
        </section>
      )}
    </>
  )
}

/**
 * Numara değişikliği: parolayla yeni giriş, yeni numaraya kod. Değişince eski numaraya ve
 * e-postaya haber gidiyor ve bankaya çekim bir süre kapanıyor.
 */
function PhoneChange() {
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const returned = params.get('yeniden') === '1'

  const [phone, setPhone] = useState(() => (returned ? readPending() : null) ?? '')
  const [code, setCode] = useState('')
  const [verification, setVerification] = useState<PhoneVerificationStarted | null>(null)
  const [done, setDone] = useState<PhoneChanged | null>(null)

  const start = useMutation({
    mutationFn: () => api.startPhoneChange(phone),
    onSuccess: setVerification,
    onError: (error) => {
      // Bir kez yönlendiriliyor; dönüşte hâlâ isteniyorsa hata görünüyor, döngü yok.
      if (needsReauthentication(error) && !returned) {
        writePending(phone)
        reauthenticate(returnedPath)
      }
    },
  })

  const confirm = useMutation({
    mutationFn: (verificationId: string) => api.confirmPhoneChange(verificationId, code),
    onSuccess: async (changed) => {
      setDone(changed)
      setVerification(null)
      writePending(null)
      await queryClient.invalidateQueries({ queryKey: statusKey })
      await queryClient.invalidateQueries({ queryKey: ['accounts'] })
    },
  })

  if (done) {
    return (
      <section className="card">
        <p className="success" role="status">
          Numaran değişti: {done.phone}.
          {done.withdrawalHoldUntil && (
            <> Güvenliğin için {date(done.withdrawalHoldUntil)} tarihine kadar banka hesabına para çekemezsin.</>
          )}
        </p>
      </section>
    )
  }

  function submitPhone(event: FormEvent) {
    event.preventDefault()
    start.mutate()
  }

  function submitCode(event: FormEvent) {
    event.preventDefault()

    if (verification) {
      confirm.mutate(verification.verificationId)
    }
  }

  return (
    <section className="card">
      <h2>Telefonu değiştir</h2>
      {verification ? (
        <form className="stacked" onSubmit={submitCode}>
          <p>{verification.phone} numarasına gelen altı haneli kodu yaz.</p>
          <label>
            Kod
            <input
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={confirm.isPending}>
            Numarayı değiştir
          </button>
          {confirm.error && <ErrorMessage error={confirm.error} />}
        </form>
      ) : (
        <form className="stacked" onSubmit={submitPhone}>
          <p className="muted">
            Parolanla yeniden giriş yapman istenebilir. Numara değişince eski numarana ve e-postana haber gidiyor;
            güvenliğin için bir süre banka hesabına para çekemiyorsun.
          </p>
          <label>
            Yeni numara
            <input
              inputMode="tel"
              autoComplete="tel"
              placeholder="05xx xxx xx xx"
              value={phone}
              onChange={(event) => setPhone(event.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={start.isPending}>
            Kod gönder
          </button>
          {start.error && !(needsReauthentication(start.error) && !returned) && <ErrorMessage error={start.error} />}
        </form>
      )}
    </section>
  )
}

function readPending(): string | null {
  try {
    return sessionStorage.getItem(pendingPhoneKey)
  } catch {
    return null
  }
}

function writePending(phone: string | null): void {
  try {
    if (phone === null) {
      sessionStorage.removeItem(pendingPhoneKey)
    } else {
      sessionStorage.setItem(pendingPhoneKey, phone)
    }
  } catch {
    // Saklanamazsa müşteri numarayı yeniden yazıyor.
  }
}

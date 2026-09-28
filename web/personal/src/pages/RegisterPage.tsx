import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api, ApiError, loginUrl } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'

type Step = 'email' | 'code' | 'password' | 'done'

/**
 * Kayıt, oturumsuz: e-posta, adrese giden kod, parola. Bitince hesap açılmış oluyor ve
 * müşteri girişe e-postası dolu gidiyor. Parola kimlik sağlayıcıya yazılıyor; ne bu
 * uygulama ne BFF saklıyor.
 */
export function RegisterPage() {
  const [step, setStep] = useState<Step>('email')
  const [email, setEmail] = useState('')
  const [registrationId, setRegistrationId] = useState('')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [passwordAgain, setPasswordAgain] = useState('')
  const [mismatch, setMismatch] = useState(false)

  const start = useMutation({
    mutationFn: () => api.startRegistration(email.trim()),
    onSuccess: (started) => {
      setRegistrationId(started.registrationId)
      setCode('')
      setStep('code')
    },
  })

  const verify = useMutation({
    mutationFn: () => api.verifyRegistrationEmail(registrationId, code.trim()),
    onSuccess: () => setStep('password'),
  })

  const complete = useMutation({
    mutationFn: () => api.completeRegistration(registrationId, password),
    onSuccess: () => setStep('done'),
  })

  function submitEmail(event: FormEvent) {
    event.preventDefault()
    start.mutate()
  }

  function submitCode(event: FormEvent) {
    event.preventDefault()
    verify.mutate()
  }

  function submitPassword(event: FormEvent) {
    event.preventDefault()
    setMismatch(password !== passwordAgain)

    if (password === passwordAgain) {
      complete.mutate()
    }
  }

  const login = loginUrl('/', email.trim())
  const alreadyRegistered = complete.error instanceof ApiError && complete.error.problem?.rule === 'email_registered'

  return (
    <main>
      <section className="card sign-in">
        <h1>HiWallet'e kayıt ol</h1>

        {step === 'email' && (
          <form className="stacked" onSubmit={submitEmail}>
            <label>
              E-posta
              <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required />
            </label>
            <button type="submit" disabled={start.isPending}>
              Kod gönder
            </button>
            {start.error && <ErrorMessage error={start.error} />}
          </form>
        )}

        {step === 'code' && (
          <form className="stacked" onSubmit={submitCode}>
            <p className="muted small">{email} adresine altı haneli bir kod gönderdik.</p>
            <label>
              Doğrulama kodu
              <input
                inputMode="numeric"
                autoComplete="one-time-code"
                pattern="[0-9]{6}"
                maxLength={6}
                value={code}
                onChange={(event) => setCode(event.target.value)}
                required
              />
            </label>
            <button type="submit" disabled={verify.isPending}>
              Doğrula
            </button>
            {/* Yeni kod yeni bir kayıt başlatıyor; eski kod geçersiz kalıyor. */}
            <button type="button" className="link" onClick={() => start.mutate()} disabled={start.isPending}>
              Kodu yeniden gönder
            </button>
            {verify.error && <ErrorMessage error={verify.error} />}
          </form>
        )}

        {step === 'password' && (
          <form className="stacked" onSubmit={submitPassword}>
            <label>
              Parola
              <input
                type="password"
                autoComplete="new-password"
                minLength={8}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </label>
            <label>
              Parola (tekrar)
              <input
                type="password"
                autoComplete="new-password"
                minLength={8}
                value={passwordAgain}
                onChange={(event) => setPasswordAgain(event.target.value)}
                required
              />
            </label>
            <p className="muted small">En az 8 karakter.</p>
            <button type="submit" disabled={complete.isPending}>
              Kaydı tamamla
            </button>
            {mismatch && (
              <div className="error" role="alert">
                <p>Parolalar aynı değil.</p>
              </div>
            )}
            {complete.error && <ErrorMessage error={complete.error} />}
            {alreadyRegistered && (
              <p>
                <a href={login}>Giriş yap</a>
              </p>
            )}
          </form>
        )}

        {step === 'done' && (
          <>
            <div className="success">
              <p>Kaydın tamamlandı, hesabın açıldı.</p>
            </div>
            <p>Giriş yaptıktan sonra telefonunu ve kimliğini doğrulayarak hesabını kullanmaya başlayabilirsin.</p>
            <a className="button" href={login}>
              Giriş yap
            </a>
          </>
        )}

        {step !== 'done' && !alreadyRegistered && (
          <p className="muted small">
            Hesabın var mı? <Link to="/">Giriş yap</Link>
          </p>
        )}
      </section>
    </main>
  )
}

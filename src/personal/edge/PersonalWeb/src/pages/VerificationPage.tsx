import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import type { OnboardingStatus } from '../types'

const statusKey = ['onboarding'] as const

/**
 * Temel doğrulama: telefon, kimlik bilgileri, sözleşme ve aydınlatma metni. Adımlar
 * sırayla; hangisinde olunduğunu sunucu söylüyor. Üçü tamamlanınca hesap para alabiliyor
 * ve işyerine ödeyebiliyor.
 */
export function VerificationPage() {
  const status = useQuery({ queryKey: statusKey, queryFn: api.onboardingStatus })

  if (status.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (status.error) {
    return <ErrorMessage error={status.error} />
  }

  const { phoneVerified, identityVerified, basicVerificationCompleted } = status.data

  return (
    <>
      <h1>Hesabını doğrula</h1>
      <ol className="steps">
        <li className={phoneVerified ? 'done' : 'current'}>Telefon</li>
        <li className={identityVerified ? 'done' : phoneVerified ? 'current' : ''}>Kimlik</li>
        <li className={basicVerificationCompleted ? 'done' : identityVerified ? 'current' : ''}>Onaylar</li>
      </ol>
      <section className="card">
        {!phoneVerified && <PhoneStep />}
        {phoneVerified && !identityVerified && <IdentityStep />}
        {phoneVerified && identityVerified && !basicVerificationCompleted && <ConsentStep status={status.data} />}
        {basicVerificationCompleted && (
          <>
            <div className="success">
              <p>Temel doğrulama tamamlandı.</p>
            </div>
            <p>Hesabına para alabilir ve işyerlerine ödeme yapabilirsin.</p>
            <Link className="button" to="/">
              Cüzdanlarım
            </Link>
          </>
        )}
      </section>
    </>
  )
}

function useStepDone() {
  const queryClient = useQueryClient()

  return async () => {
    await queryClient.invalidateQueries({ queryKey: statusKey })
    await queryClient.invalidateQueries({ queryKey: ['accounts'] })
  }
}

function PhoneStep() {
  const done = useStepDone()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [verificationId, setVerificationId] = useState<string | null>(null)
  const [maskedPhone, setMaskedPhone] = useState('')

  const start = useMutation({
    mutationFn: () => api.startPhoneVerification(phone.trim()),
    onSuccess: (started) => {
      setVerificationId(started.verificationId)
      setMaskedPhone(started.phone)
      setCode('')
    },
  })

  const confirm = useMutation({
    mutationFn: () => api.confirmPhone(verificationId!, code.trim()),
    onSuccess: done,
  })

  function submitPhone(event: FormEvent) {
    event.preventDefault()
    start.mutate()
  }

  function submitCode(event: FormEvent) {
    event.preventDefault()
    confirm.mutate()
  }

  if (verificationId === null) {
    return (
      <form className="stacked" onSubmit={submitPhone}>
        <label>
          Cep telefonu
          <input
            type="tel"
            autoComplete="tel"
            placeholder="05XX XXX XX XX"
            value={phone}
            onChange={(event) => setPhone(event.target.value)}
            required
          />
        </label>
        <button type="submit" disabled={start.isPending}>
          Kod gönder
        </button>
        {start.error && <ErrorMessage error={start.error} />}
      </form>
    )
  }

  return (
    <form className="stacked" onSubmit={submitCode}>
      <p className="muted small">{maskedPhone} numarasına SMS ile altı haneli bir kod gönderdik.</p>
      <label>
        SMS kodu
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
      <button type="submit" disabled={confirm.isPending}>
        Doğrula
      </button>
      <button type="button" className="link" onClick={() => setVerificationId(null)}>
        Numarayı değiştir
      </button>
      {confirm.error && <ErrorMessage error={confirm.error} />}
    </form>
  )
}

function IdentityStep() {
  const done = useStepDone()
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [nationalId, setNationalId] = useState('')
  const [birthDate, setBirthDate] = useState('')

  const verify = useMutation({
    mutationFn: () =>
      api.verifyIdentity({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        nationalId: nationalId.trim(),
        birthDate,
      }),
    onSuccess: done,
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    verify.mutate()
  }

  return (
    <form className="stacked" onSubmit={submit}>
      <p className="muted small">Bilgiler nüfus kaydıyla karşılaştırılıyor; kimlik kartındaki gibi yaz.</p>
      <label>
        Ad
        <input autoComplete="given-name" value={firstName} onChange={(event) => setFirstName(event.target.value)} required />
      </label>
      <label>
        Soyad
        <input autoComplete="family-name" value={lastName} onChange={(event) => setLastName(event.target.value)} required />
      </label>
      <label>
        T.C. kimlik numarası
        <input
          inputMode="numeric"
          pattern="[0-9]{11}"
          maxLength={11}
          value={nationalId}
          onChange={(event) => setNationalId(event.target.value)}
          required
        />
      </label>
      <label>
        Doğum tarihi
        <input type="date" autoComplete="bday" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} required />
      </label>
      <button type="submit" disabled={verify.isPending}>
        Kimliği doğrula
      </button>
      {verify.error && <ErrorMessage error={verify.error} />}
    </form>
  )
}

function ConsentStep({ status }: { status: OnboardingStatus }) {
  const done = useStepDone()
  const [terms, setTerms] = useState(false)
  const [privacyNotice, setPrivacyNotice] = useState(false)
  const [missing, setMissing] = useState(false)
  const { termsVersion, privacyNoticeVersion } = status.documents

  const accept = useMutation({
    mutationFn: () => api.completeBasicVerification(termsVersion, privacyNoticeVersion),
    onSuccess: done,
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setMissing(!terms || !privacyNotice)

    if (terms && privacyNotice) {
      accept.mutate()
    }
  }

  return (
    <form className="stacked" onSubmit={submit}>
      <label className="check">
        <input type="checkbox" checked={terms} onChange={(event) => setTerms(event.target.checked)} />
        Kullanıcı sözleşmesini (sürüm {termsVersion}) okudum ve kabul ediyorum.
      </label>
      <label className="check">
        <input type="checkbox" checked={privacyNotice} onChange={(event) => setPrivacyNotice(event.target.checked)} />
        KVKK aydınlatma metnini (sürüm {privacyNoticeVersion}) okudum.
      </label>
      <button type="submit" disabled={accept.isPending}>
        Onayla
      </button>
      {missing && (
        <div className="error" role="alert">
          <p>Devam etmek için iki metni de onayla.</p>
        </div>
      )}
      {accept.error && <ErrorMessage error={accept.error} />}
    </form>
  )
}

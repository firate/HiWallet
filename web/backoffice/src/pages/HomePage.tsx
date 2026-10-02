import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'

const targets = {
  hesaplar: 'Hesap',
  cuzdanlar: 'Cüzdan',
  cekimler: 'Çekim',
  kampanyalar: 'Kampanya',
} as const

type Target = keyof typeof targets

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Kayda kimliğiyle gidiliyor. E-posta, telefon ya da TCKN ile arama yok: o veri
 * onboarding'de ve panelin oraya bağlantısı yok.
 */
export function HomePage() {
  const navigate = useNavigate()
  const [target, setTarget] = useState<Target>('hesaplar')
  const [id, setId] = useState('')
  const [invalid, setInvalid] = useState(false)

  function open(event: FormEvent) {
    event.preventDefault()
    const value = id.trim()

    if (!guid.test(value)) {
      setInvalid(true)
      return
    }

    navigate(`/${target}/${value}`)
  }

  return (
    <>
      <section className="card">
        <h1>Kayıt aç</h1>
        <form className="inline" onSubmit={open}>
          <select aria-label="Kayıt türü" value={target} onChange={(event) => setTarget(event.target.value as Target)}>
            {Object.entries(targets).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
          <input
            aria-label="Kimlik"
            className="grow"
            placeholder="00000000-0000-0000-0000-000000000000"
            value={id}
            onChange={(event) => {
              setId(event.target.value)
              setInvalid(false)
            }}
          />
          <button type="submit">Aç</button>
        </form>
        {invalid && (
          <div className="error" role="alert">
            <p>Kimlik bir GUID olmalı.</p>
          </div>
        )}
        <p className="muted small">
          Müşteriyi e-posta ya da telefonla aramak henüz yok; kişisel veri onboarding'de duruyor.
        </p>
      </section>
      <section className="card">
        <h2>İş kuyrukları</h2>
        <ul className="links">
          <li>
            <Link to="/cekimler?durum=under_review">İncelemede bekleyen çekimler</Link>
          </li>
          <li>
            <Link to="/kampanyalar">Promo kampanyaları</Link>
          </li>
        </ul>
      </section>
    </>
  )
}

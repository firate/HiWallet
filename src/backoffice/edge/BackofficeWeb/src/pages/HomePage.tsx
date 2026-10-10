import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { useStaffAccess } from '../session'
import type { CustomerSearch, StaffPermission } from '../types'

/** Açılabilen kayıtlar ve görüntülemek için gereken izin. */
const targets = {
  hesaplar: { label: 'Hesap', permission: 'customer.view' },
  cuzdanlar: { label: 'Cüzdan', permission: 'customer.view' },
  cekimler: { label: 'Çekim', permission: 'customer.view' },
  kampanyalar: { label: 'Kampanya', permission: 'campaign.view' },
} as const satisfies Record<string, { label: string; permission: StaffPermission }>

type Target = keyof typeof targets

/** İş kuyrukları ve görmek için gereken izin. */
const queues: { to: string; label: string; permission: StaffPermission }[] = [
  { to: '/cekimler?durum=under_review', label: 'İncelemede bekleyen çekimler', permission: 'customer.view' },
  { to: '/havaleler', label: 'Askıdaki havaleler', permission: 'deposit.view' },
  { to: '/kampanyalar', label: 'Promo kampanyaları', permission: 'campaign.view' },
]

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** On hane; gruplama boşlukları atılıyor. Kontrol hanesine wallet-api bakıyor. */
const accountNumber = /^[0-9]{10}$/

/**
 * Kayıt arama ve iş kuyrukları, çalışanın izinlerine göre: izni olmayan işe götüren bir
 * bağlantı yalnızca reddedilen bir istek üretirdi.
 */
export function HomePage() {
  const { permissions } = useStaffAccess()
  const allowedTargets = (Object.keys(targets) as Target[]).filter((target) =>
    permissions.includes(targets[target].permission),
  )
  const allowedQueues = queues.filter((queue) => permissions.includes(queue.permission))

  return (
    <>
      {allowedTargets.length > 0 && <Search targets={allowedTargets} />}
      {permissions.includes('customer.view') && <CustomerSearchForm />}
      {allowedQueues.length > 0 && (
        <section className="card">
          <h2>İş kuyrukları</h2>
          <ul className="links">
            {allowedQueues.map((queue) => (
              <li key={queue.to}>
                <Link to={queue.to}>{queue.label}</Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

/** Hesaba hesap numarasıyla, diğer kayıtlara kimliğiyle gidiliyor. */
function Search({ targets: allowed }: { targets: Target[] }) {
  const navigate = useNavigate()
  const [target, setTarget] = useState<Target>(allowed[0])
  const [id, setId] = useState('')
  const [invalid, setInvalid] = useState(false)
  const lookup = useMutation({
    mutationFn: api.accountByNumber,
    onSuccess: (account) => navigate(`/hesaplar/${account.accountId}`),
  })

  function open(event: FormEvent) {
    event.preventDefault()
    const value = id.trim()
    const digits = value.replaceAll(' ', '')

    if (target === 'hesaplar' && accountNumber.test(digits)) {
      lookup.mutate(digits)
      return
    }

    if (!guid.test(value)) {
      setInvalid(true)
      return
    }

    navigate(`/${target}/${value}`)
  }

  return (
    <section className="card">
      <h1>Kayıt aç</h1>
      <form className="inline" onSubmit={open}>
        <select
          aria-label="Kayıt türü"
          value={target}
          onChange={(event) => {
            setTarget(event.target.value as Target)
            setInvalid(false)
            lookup.reset()
          }}
        >
          {allowed.map((value) => (
            <option key={value} value={value}>
              {targets[value].label}
            </option>
          ))}
        </select>
        <input
          aria-label="Kimlik"
          className="grow"
          placeholder={target === 'hesaplar' ? 'Hesap numarası ya da kimlik' : '00000000-0000-0000-0000-000000000000'}
          value={id}
          onChange={(event) => {
            setId(event.target.value)
            setInvalid(false)
            lookup.reset()
          }}
        />
        <button type="submit" disabled={lookup.isPending}>
          Aç
        </button>
      </form>
      {invalid && (
        <div className="error" role="alert">
          <p>{target === 'hesaplar' ? 'Hesap numarası on hane, kimlik bir GUID olmalı.' : 'Kimlik bir GUID olmalı.'}</p>
        </div>
      )}
      {lookup.error && <ErrorMessage error={lookup.error} />}
    </section>
  )
}

const criteria = {
  email: { label: 'E-posta', placeholder: 'ad@ornek.com' },
  phone: { label: 'Telefon', placeholder: '0532 123 45 67' },
  nationalId: { label: 'T.C. kimlik no', placeholder: '11 hane' },
} as const

type Criterion = keyof typeof criteria

/**
 * Müşteriyi e-posta, telefon ya da kimlik numarasıyla bulmak; kişisel veri onboarding'de.
 * Biçimi onboarding doğruluyor, panel yalnızca boşlukları atıyor. Sonuçta telefon maskeli.
 */
function CustomerSearchForm() {
  const [criterion, setCriterion] = useState<Criterion>('email')
  const [value, setValue] = useState('')
  const search = useMutation({ mutationFn: (request: CustomerSearch) => api.searchCustomers(request) })

  function submit(event: FormEvent) {
    event.preventDefault()
    const trimmed = value.trim()

    search.mutate(
      criterion === 'email' ? { email: trimmed } : criterion === 'phone' ? { phone: trimmed } : { nationalId: trimmed },
    )
  }

  return (
    <section className="card">
      <h2>Müşteri ara</h2>
      <form className="inline" onSubmit={submit}>
        <select
          aria-label="Arama ölçütü"
          value={criterion}
          onChange={(event) => {
            setCriterion(event.target.value as Criterion)
            search.reset()
          }}
        >
          {(Object.keys(criteria) as Criterion[]).map((key) => (
            <option key={key} value={key}>
              {criteria[key].label}
            </option>
          ))}
        </select>
        <input
          aria-label="Aranan"
          className="grow"
          placeholder={criteria[criterion].placeholder}
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
        <button type="submit" disabled={search.isPending}>
          Ara
        </button>
      </form>
      {search.error && <ErrorMessage error={search.error} />}
      {search.data &&
        (search.data.items.length === 0 ? (
          <p className="muted">Eşleşen müşteri yok.</p>
        ) : (
          <ul className="links">
            {search.data.items.map((match) => (
              <li key={match.accountId}>
                <Link to={`/hesaplar/${match.accountId}`}>
                  {[match.firstName, match.lastName].filter(Boolean).join(' ') || match.email}
                  <span className="muted small">
                    {' '}
                    {match.email}
                    {match.phone && ` · ${match.phone}`}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        ))}
    </section>
  )
}

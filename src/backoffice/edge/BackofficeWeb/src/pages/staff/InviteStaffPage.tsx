import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { api } from '../../api'
import { ErrorMessage } from '../../components/ErrorMessage'
import { RoleChecklist } from './RoleChecklist'
import { StaffSection } from './StaffSection'

/**
 * Çalışanı açar ve davet e-postasını gönderir. Parolasını ve OTP'sini çalışan davetteki
 * bağlantıdan kendisi kuruyor; panel parolayı hiç görmüyor.
 */
export function InviteStaffPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [email, setEmail] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [roleIds, setRoleIds] = useState<string[]>([])

  const invite = useMutation({
    mutationFn: () =>
      api.inviteStaff({
        email: email.trim(),
        firstName: firstName.trim() || null,
        lastName: lastName.trim() || null,
        roleIds,
      }),
    onSuccess: async (staff) => {
      await queryClient.invalidateQueries({ queryKey: ['staff', 'list'] })
      navigate(`/personel/${staff.staffId}`)
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    invite.mutate()
  }

  return (
    <StaffSection>
      <section className="card">
        <p>
          <Link to="/personel">Çalışanlar</Link>
        </p>
        <h1>Çalışan davet et</h1>
        <form className="stacked" onSubmit={submit}>
          <label>
            E-posta
            <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required />
          </label>
          <label>
            Ad
            <input value={firstName} onChange={(event) => setFirstName(event.target.value)} maxLength={100} />
          </label>
          <label>
            Soyad
            <input value={lastName} onChange={(event) => setLastName(event.target.value)} maxLength={100} />
          </label>
          <RoleChecklist selected={roleIds} onChange={setRoleIds} />
          <p className="muted small">
            Rolü olmayan çalışan girebilir ama panelde bir şey göremez. Davet bağlantısı 72 saat geçerli.
          </p>
          <button type="submit" disabled={invite.isPending}>
            Davet gönder
          </button>
        </form>
        {invite.error && <ErrorMessage error={invite.error} />}
      </section>
    </StaffSection>
  )
}

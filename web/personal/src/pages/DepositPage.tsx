import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api } from '../api'
import { CopyButton } from '../components/CopyButton'
import { ErrorMessage } from '../components/ErrorMessage'
import { accountNumber, iban } from '../format'

/**
 * Havaleyle para yükleme. Bütün müşteriler aynı IBAN'a gönderiyor; paranın kime ait
 * olduğunu açıklamadaki hesap numarası söylüyor. Gelen para bu para birimindeki
 * varsayılan cüzdana düşüyor. Yalnızca müşterinin kendi adına kayıtlı banka hesabından
 * gelen havale cüzdana geçiyor.
 */
export function DepositPage() {
  const { accountId = '' } = useParams()
  const account = useQuery({ queryKey: ['accounts', accountId], queryFn: () => api.account(accountId) })
  const instructions = useQuery({
    queryKey: ['accounts', accountId, 'deposit-instructions'],
    queryFn: () => api.depositInstructions(accountId),
  })

  if (account.isPending || instructions.isPending) {
    return <p className="muted">Yükleniyor...</p>
  }

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  if (instructions.error) {
    return <ErrorMessage error={instructions.error} />
  }

  const back = (
    <p>
      <Link to="/">Cüzdanlarım</Link>
    </p>
  )

  // Doğrulanmamış hesabın limiti sıfır: gelen havale cüzdana geçmezdi.
  if (account.data.kycLevel === 'Unknown') {
    return (
      <>
        {back}
        <section className="card">
          <h1>Havaleyle para yükle</h1>
          <div className="notice">
            <p>Para yükleyebilmek için önce telefonunu ve kimliğini doğrula.</p>
            <Link className="button" to="/dogrulama">
              Doğrulamayı tamamla
            </Link>
          </div>
        </section>
      </>
    )
  }

  const { accountHolder, currency, reference } = instructions.data

  return (
    <>
      {back}
      <section className="card">
        <h1>Havaleyle para yükle</h1>
        <p>
          Kendi bankandan bu hesaba havale ya da FAST gönder. Açıklamaya hesap numaranı yaz; para {currency}{' '}
          cüzdanına geçer.
        </p>
        <dl>
          <dt>Alıcı</dt>
          <dd>
            {accountHolder} <CopyButton text={accountHolder} label="Alıcıyı kopyala" />
          </dd>
          <dt>IBAN</dt>
          <dd>
            <span className="account-number">{iban(instructions.data.iban)}</span>{' '}
            <CopyButton text={instructions.data.iban} label="IBAN'ı kopyala" />
          </dd>
          <dt>Açıklama</dt>
          <dd>
            <span className="account-number">{accountNumber(reference)}</span>{' '}
            <CopyButton text={reference} label="Hesap numarasını kopyala" />
          </dd>
        </dl>
        <div className="notice">
          <p>
            Yalnızca kendi adına kayıtlı banka hesabından gönder. Başka birinin hesabından gelen, açıklamasında hesap
            numaran olmayan ya da doğrulama seviyenin limitini aşan para cüzdanına geçmez.
          </p>
        </div>
      </section>
    </>
  )
}

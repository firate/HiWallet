import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../api'
import { ErrorMessage } from '../components/ErrorMessage'
import { useIdempotencyKey } from '../idempotency'
import { openPaymentPage } from '../paymentPage'

/**
 * Kartla para yükleme. Limit ödemeden ÖNCE kontrol ediliyor: yetmiyorsa ödeme açılmıyor
 * ve kart sorulmuyor. Yetiyorsa müşteri kart sağlayıcısının ödeme sayfasına gidiyor;
 * sonuç dönüşte `/kart-yukleme`'de izleniyor.
 */
export function CardTopupPage() {
  const { walletId = '' } = useParams()
  const navigate = useNavigate()
  const wallet = useQuery({ queryKey: ['wallets', walletId], queryFn: () => api.wallet(walletId) })
  const accountId = wallet.data?.accountId
  const account = useQuery({
    queryKey: ['accounts', accountId],
    queryFn: () => api.account(accountId ?? ''),
    enabled: accountId !== undefined,
  })

  const [amount, setAmount] = useState('')
  const [idempotencyKey] = useIdempotencyKey()

  const start = useMutation({
    mutationFn: (currency: string) =>
      api.startCardTopup({ walletId, amount: Number(amount), currency }, idempotencyKey),
    onSuccess: (topup) => {
      // Aynı anahtarla tekrar edilen istek sonucu belli olan yüklemeyi dönebilir: onun
      // için ödeme yeniden istenmiyor, sonucu gösteriliyor.
      if (topup.state === 'pending' && topup.paymentUrl) {
        openPaymentPage(topup.paymentUrl)
      } else {
        navigate(`/kart-yukleme?cardTopupId=${topup.cardTopupId}`)
      }
    },
  })

  if (wallet.error) {
    return <ErrorMessage error={wallet.error} />
  }

  if (account.error) {
    return <ErrorMessage error={account.error} />
  }

  if (!wallet.data || !account.data) {
    return <p className="muted">Yükleniyor...</p>
  }

  const { name, currency } = wallet.data

  const back = (
    <p>
      <Link to={`/cuzdanlar/${walletId}`}>{name}</Link>
    </p>
  )

  // Doğrulanmamış hesabın gelen para limiti sıfır: ödeme açılmazdı.
  if (account.data.kycLevel === 'Unknown') {
    return (
      <>
        {back}
        <section className="card">
          <h1>Kartla para yükle</h1>
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

  function submit(event: FormEvent) {
    event.preventDefault()
    start.mutate(currency)
  }

  return (
    <>
      {back}
      <section className="card">
        <h1>Kartla para yükle</h1>
        <p className="muted">
          Kart bilgilerini kart sağlayıcısının sayfasında gireceksin. Ödemeden sonra buraya dönüyorsun; para bu
          cüzdana geçiyor.
        </p>
        <form className="stacked" onSubmit={submit}>
          <label>
            Tutar ({currency})
            <input
              type="number"
              inputMode="decimal"
              min="0.01"
              step="0.01"
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
              required
            />
          </label>
          <button type="submit" disabled={start.isPending || start.isSuccess}>
            Ödemeye geç
          </button>
        </form>
        {start.error && <ErrorMessage error={start.error} />}
      </section>
    </>
  )
}

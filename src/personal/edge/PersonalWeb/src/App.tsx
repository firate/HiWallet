import { Link, Navigate, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { AccountsPage } from './pages/AccountsPage'
import { CardTopupPage } from './pages/CardTopupPage'
import { CardTopupStatusPage } from './pages/CardTopupStatusPage'
import { DepositPage } from './pages/DepositPage'
import { LimitsPage } from './pages/LimitsPage'
import { RegisterPage } from './pages/RegisterPage'
import { TransferPage } from './pages/TransferPage'
import { VerificationPage } from './pages/VerificationPage'
import { WalletPage } from './pages/WalletPage'
import { WithdrawalPage } from './pages/WithdrawalPage'
import { WithdrawalStatusPage } from './pages/WithdrawalStatusPage'
import { SessionGate } from './session'

export function App() {
  return (
    <Routes>
      {/* Kayıt oturumsuz: müşterinin henüz kullanıcısı yok. */}
      <Route path="kayit" element={<RegisterPage />} />
      <Route
        path="*"
        element={
          <SessionGate>
            <SignedInRoutes />
          </SessionGate>
        }
      />
    </Routes>
  )
}

function SignedInRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<AccountsPage />} />
        <Route path="dogrulama" element={<VerificationPage />} />
        <Route path="hesaplar/:accountId/yukle" element={<DepositPage />} />
        <Route path="hesaplar/:accountId/limitler" element={<LimitsPage />} />
        <Route path="cuzdanlar" element={<Navigate to="/" replace />} />
        <Route path="cuzdanlar/:walletId" element={<WalletPage />} />
        <Route path="cuzdanlar/:walletId/transfer" element={<TransferPage />} />
        <Route path="cuzdanlar/:walletId/cekim" element={<WithdrawalPage />} />
        <Route path="cuzdanlar/:walletId/kartla-yukle" element={<CardTopupPage />} />
        {/* Ödeme sayfasının dönüş adresi; BFF bu yolu kart sağlayıcısına veriyor. */}
        <Route path="kart-yukleme" element={<CardTopupStatusPage />} />
        <Route path="cekimler/:withdrawalId" element={<WithdrawalStatusPage />} />
        <Route
          path="*"
          element={
            <p>
              Sayfa bulunamadı. <Link to="/">Cüzdanlarım</Link>
            </p>
          }
        />
      </Route>
    </Routes>
  )
}

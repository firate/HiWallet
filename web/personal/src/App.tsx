import { Link, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { AccountsPage } from './pages/AccountsPage'
import { TransferPage } from './pages/TransferPage'
import { WalletPage } from './pages/WalletPage'
import { WithdrawalPage } from './pages/WithdrawalPage'
import { WithdrawalStatusPage } from './pages/WithdrawalStatusPage'
import { SessionGate } from './session'

export function App() {
  return (
    <SessionGate>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<AccountsPage />} />
          <Route path="cuzdanlar/:walletId" element={<WalletPage />} />
          <Route path="cuzdanlar/:walletId/transfer" element={<TransferPage />} />
          <Route path="cuzdanlar/:walletId/cekim" element={<WithdrawalPage />} />
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
    </SessionGate>
  )
}

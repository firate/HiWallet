import { Link, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { AccountPage } from './pages/AccountPage'
import { CampaignPage } from './pages/CampaignPage'
import { CampaignsPage } from './pages/CampaignsPage'
import { HomePage } from './pages/HomePage'
import { NewCampaignPage } from './pages/NewCampaignPage'
import { SuspendedDepositsPage } from './pages/SuspendedDepositsPage'
import { AuditPage } from './pages/staff/AuditPage'
import { InviteStaffPage } from './pages/staff/InviteStaffPage'
import { NewRolePage, RolePage, RolesPage } from './pages/staff/RolePages'
import { StaffListPage } from './pages/staff/StaffListPage'
import { StaffPage } from './pages/staff/StaffPage'
import { WalletPage } from './pages/WalletPage'
import { WithdrawalPage } from './pages/WithdrawalPage'
import { WithdrawalsPage } from './pages/WithdrawalsPage'
import { SessionGate } from './session'

/** Panelin bütün sayfaları oturumla; çalışanın kaydı yok, kullanıcıyı yönetici açıyor. */
export function App() {
  return (
    <SessionGate>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<HomePage />} />
          <Route path="hesaplar/:accountId" element={<AccountPage />} />
          <Route path="cuzdanlar/:walletId" element={<WalletPage />} />
          <Route path="cekimler" element={<WithdrawalsPage />} />
          <Route path="cekimler/:withdrawalId" element={<WithdrawalPage />} />
          <Route path="havaleler" element={<SuspendedDepositsPage />} />
          <Route path="kampanyalar" element={<CampaignsPage />} />
          <Route path="kampanyalar/yeni" element={<NewCampaignPage />} />
          <Route path="kampanyalar/:campaignId" element={<CampaignPage />} />
          <Route path="personel" element={<StaffListPage />} />
          <Route path="personel/yeni" element={<InviteStaffPage />} />
          <Route path="personel/kayitlar" element={<AuditPage />} />
          <Route path="personel/:staffId" element={<StaffPage />} />
          <Route path="roller" element={<RolesPage />} />
          <Route path="roller/yeni" element={<NewRolePage />} />
          <Route path="roller/:roleId" element={<RolePage />} />
          <Route
            path="*"
            element={
              <p>
                Sayfa bulunamadı. <Link to="/">Ana sayfa</Link>
              </p>
            }
          />
        </Route>
      </Routes>
    </SessionGate>
  )
}

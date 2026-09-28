import type { ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import type { UserRole } from './api/types'
import { useAuth } from './auth/useAuth'
import { homePathFor } from './auth/roles'
import PinPage from './pages/PinPage'
import SetupPage from './pages/SetupPage'
import StatusPage from './pages/StatusPage'
import TableOrderPage from './pages/TableOrderPage'
import TablesPage from './pages/TablesPage'
import TicketsPage from './pages/TicketsPage'

/**
 * Sayfa yönlendirmesi. Kapı bekçileri sırayla şunu sağlar:
 * cihaz eşleştirilmemişse kurulum ekranı → PIN girilmemişse PIN ekranı → rol uygun değilse kendi ekranı.
 */
export default function App() {
  return (
    <Routes>
      <Route path="/durum" element={<StatusPage />} />
      <Route path="/kurulum" element={<SetupPage />} />
      <Route path="/giris" element={<RequireDevice><PinPage /></RequireDevice>} />

      <Route path="/garson" element={<RequireUser roles={['Owner', 'Manager', 'Waiter', 'Cashier']}><TablesPage /></RequireUser>} />
      <Route path="/garson/masa/:tableId" element={<RequireUser roles={['Owner', 'Manager', 'Waiter', 'Cashier']}><TableOrderPage /></RequireUser>} />
      <Route path="/fisler" element={<RequireUser roles={['Owner', 'Manager', 'Kitchen']}><TicketsPage /></RequireUser>} />

      <Route path="*" element={<Home />} />
    </Routes>
  )
}

/** Ana adres: duruma göre kurulum, PIN veya kullanıcının kendi ekranı. */
function Home() {
  const { device, user } = useAuth()
  if (!device) return <Navigate to="/kurulum" replace />
  if (!user) return <Navigate to="/giris" replace />
  return <Navigate to={homePathFor(user.role)} replace />
}

function RequireDevice({ children }: { children: ReactNode }) {
  const { device, user } = useAuth()
  if (!device) return <Navigate to="/kurulum" replace />
  // Zaten giriş yapılmışsa PIN ekranını atla.
  if (user) return <Navigate to={homePathFor(user.role)} replace />
  return children
}

function RequireUser({ roles, children }: { roles: UserRole[]; children: ReactNode }) {
  const { device, user } = useAuth()
  if (!device) return <Navigate to="/kurulum" replace />
  if (!user) return <Navigate to="/giris" replace />
  if (!roles.includes(user.role)) return <Navigate to={homePathFor(user.role)} replace />
  return children
}

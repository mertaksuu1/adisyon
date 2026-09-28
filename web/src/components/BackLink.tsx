import { Link } from 'react-router'
import { homePathFor, isManagement } from '../auth/roles'
import { useAuth } from '../auth/useAuth'

/** Üst çubuğun solundaki geri düğmesi. */
export function BackLink({ to, children }: { to: string; children: string }) {
  return (
    <Link to={to} className="rounded-xl bg-stone-100 px-3 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-200">
      ← {children}
    </Link>
  )
}

/**
 * Kullanıcının açılış ekranına dönen düğme: sahip/yönetici için "Ana menü", diğerleri için "Masalar".
 * Sayfa zaten kullanıcının açılış ekranıysa (ör. mutfak personeli için Fişler) hiçbir şey göstermez.
 */
export function HomeBackLink({ currentPath }: { currentPath: string }) {
  const { user } = useAuth()
  if (!user) return null
  const home = homePathFor(user.role)
  if (home === currentPath) return null
  return <BackLink to={home}>{isManagement(user.role) ? 'Ana menü' : 'Masalar'}</BackLink>
}

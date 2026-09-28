import type { ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { roleLabels } from '../auth/roles'

/** Her personel ekranının üstündeki çubuk: şube, giriş yapan kişi ve büyük "Çıkış" düğmesi. */
export function TopBar({ title, left }: { title: string; left?: ReactNode }) {
  const { device, user, logout } = useAuth()

  return (
    <header className="sticky top-0 z-10 flex items-center gap-3 border-b border-stone-200 bg-white px-4 py-3">
      {left}
      <div className="min-w-0 flex-1">
        <h1 className="truncate text-lg font-bold text-stone-900">{title}</h1>
        <p className="truncate text-xs text-stone-500">{device?.branchName}</p>
      </div>
      {user && (
        <div className="hidden text-right sm:block">
          <p className="text-sm font-medium text-stone-900">{user.displayName}</p>
          <p className="text-xs text-stone-500">{roleLabels[user.role]}</p>
        </div>
      )}
      <button
        type="button"
        onClick={logout}
        className="rounded-xl bg-stone-900 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-stone-700"
      >
        Çıkış
      </button>
    </header>
  )
}

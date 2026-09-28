import type { ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { roleLabels } from '../auth/roles'
import { useConnectionStatus } from '../realtime/useRealtime'

/**
 * Her personel ekranının üstündeki çubuk: şube, giriş yapan kişi ve büyük "Çıkış" düğmesi.
 * Sunucuyla bağlantı koparsa altında uyarı şeridi çıkar; ekran güncel olmayabilir.
 */
export function TopBar({ title, left }: { title: string; left?: ReactNode }) {
  const { device, user, logout } = useAuth()
  const connection = useConnectionStatus()

  return (
    <div className="sticky top-0 z-10">
      <header className="flex items-center gap-3 border-b border-stone-200 bg-white px-4 py-3">
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
      {connection === 'reconnecting' && (
        <p className="bg-orange-600 px-4 py-2 text-center text-sm font-semibold text-white" role="alert">
          Sunucuyla bağlantı koptu, yeniden bağlanılıyor… Ekrandaki bilgiler güncel olmayabilir.
        </p>
      )}
    </div>
  )
}

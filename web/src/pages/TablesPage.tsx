import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router'
import { api } from '../api/client'
import type { Table } from '../api/types'
import { TopBar } from '../components/TopBar'
import { formatDuration, formatMoney } from '../lib/format'

/** Masa planı: şubedeki tüm masalar, boş/dolu durumlarıyla. Masaya dokununca sipariş ekranı açılır. */
export default function TablesPage() {
  const location = useLocation()
  const navigate = useNavigate()
  const flash = (location.state as { flash?: string } | null)?.flash

  // "Sipariş gönderildi" bildirimi 4 sn sonra kaybolsun; sayfa yenilenince de tekrar çıkmasın.
  useEffect(() => {
    if (!flash) return
    const timer = setTimeout(() => navigate('.', { replace: true, state: null }), 4000)
    return () => clearTimeout(timer)
  }, [flash, navigate])

  const tables = useQuery({
    queryKey: ['tables'],
    queryFn: () => api<Table[]>('GET', '/tables'),
    // Faz 2b'de SignalR gelince buna gerek kalmayacak; şimdilik 10 sn'de bir yenile.
    refetchInterval: 10_000,
  })

  const active = tables.data?.filter((t) => t.isActive) ?? []
  const occupied = active.filter((t) => t.openSession).length

  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar title="Masalar" />

      <main className="mx-auto max-w-6xl p-4">
        {flash && (
          <p className="mb-4 rounded-xl bg-green-50 px-4 py-3 font-medium text-green-800 ring-1 ring-green-200" role="status">
            {flash}
          </p>
        )}

        <p className="mb-3 text-sm text-stone-600">
          {tables.isSuccess && `${occupied} dolu · ${active.length - occupied} boş`}
        </p>

        {tables.isPending && <p className="text-stone-500">Masalar yükleniyor…</p>}
        {tables.isError && <p className="text-red-700">{tables.error.message}</p>}

        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
          {active.map((table) => (
            <li key={table.id}>
              <TableCard table={table} />
            </li>
          ))}
        </ul>
      </main>
    </div>
  )
}

function TableCard({ table }: { table: Table }) {
  const session = table.openSession
  return (
    <Link
      to={`/garson/masa/${table.id}`}
      className={`flex aspect-[4/3] flex-col justify-between rounded-2xl p-4 shadow-sm transition active:scale-[0.98] ${
        session ? 'bg-amber-700 text-white' : 'bg-white text-stone-900 ring-1 ring-stone-200 hover:ring-amber-400'
      }`}
    >
      <span className="text-xl font-bold">{table.name}</span>
      {session ? (
        <span>
          <span className="block text-2xl font-semibold tabular-nums">{formatMoney(session.total)}</span>
          <span className="text-sm text-amber-100">{formatDuration(session.openedAt)}</span>
        </span>
      ) : (
        <span className="text-sm text-stone-400">Boş</span>
      )}
    </Link>
  )
}

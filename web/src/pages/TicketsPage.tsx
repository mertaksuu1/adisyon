import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { PrintedTicket } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { TopBar } from '../components/TopBar'
import { formatTime } from '../lib/format'

/**
 * Mutfak fişleri (sanal yazıcı). Yazıcı bağlanana kadar fişlerin kâğıtta nasıl görüneceğini gösterir.
 * Yeni sipariş gelince liste kendiliğinden yenilenir.
 */
export default function TicketsPage() {
  const { user } = useAuth()
  const tickets = useQuery({
    queryKey: ['tickets'],
    queryFn: () => api<PrintedTicket[]>('GET', '/print/recent'),
  })

  return (
    <div className="min-h-screen bg-stone-200">
      <TopBar
        title="Mutfak fişleri"
        left={
          user?.role !== 'Kitchen' && (
            <Link to="/garson" className="rounded-xl bg-stone-100 px-3 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-200">
              ← Masalar
            </Link>
          )
        }
      />

      <main className="mx-auto max-w-6xl p-4">
        <p className="mb-4 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
          Yazıcı henüz bağlı değil. Mutfak fişleri burada, kâğıtta çıkacakları hâliyle gösteriliyor.
        </p>

        {tickets.isPending && <p className="text-stone-500">Yükleniyor…</p>}
        {tickets.isError && <p className="text-red-700">{tickets.error.message}</p>}
        {tickets.isSuccess && tickets.data.length === 0 && (
          <p className="text-stone-500">Henüz fiş yok. Bir masaya sipariş gönderildiğinde burada görünecek.</p>
        )}

        <ul className="flex flex-wrap items-start gap-4">
          {tickets.data?.map((t) => (
            <li key={t.orderId}>
              <p className="mb-1 text-xs text-stone-500">
                {t.tableName} · {formatTime(t.printedAt)}
              </p>
              {/* 48 karakterlik termal fiş: sabit genişlikli yazı ve beyaz kâğıt şeridi */}
              <pre className="w-fit overflow-x-auto bg-white px-3 py-4 font-mono text-xs leading-snug text-black shadow-md">
                {t.text}
              </pre>
            </li>
          ))}
        </ul>
      </main>
    </div>
  )
}

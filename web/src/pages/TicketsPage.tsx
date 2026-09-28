import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { PrintedTicket } from '../api/types'
import { HomeBackLink } from '../components/BackLink'
import { TopBar } from '../components/TopBar'
import { formatTime } from '../lib/format'

/**
 * Fişler (sanal yazıcı): mutfak ve hesap fişleri. Yazıcı bağlanana kadar fişlerin kâğıtta nasıl görüneceğini gösterir.
 * Yeni sipariş gelince liste kendiliğinden yenilenir.
 */
const ticketKindLabels: Record<PrintedTicket['kind'], string> = { Kitchen: 'Mutfak', Bill: 'Hesap', Report: 'Rapor' }

export default function TicketsPage() {
  const tickets = useQuery({
    queryKey: ['tickets'],
    queryFn: () => api<PrintedTicket[]>('GET', '/print/recent'),
  })

  return (
    <div className="min-h-screen bg-stone-200">
      <TopBar
        title="Fişler"
        left={<HomeBackLink currentPath="/fisler" />}
      />

      <main className="mx-auto max-w-6xl p-4">
        <p className="mb-4 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
          Yazıcı henüz bağlı değil. Mutfak ve hesap fişleri burada, kâğıtta çıkacakları hâliyle gösteriliyor.
        </p>

        {tickets.isPending && <p className="text-stone-500">Yükleniyor…</p>}
        {tickets.isError && <p className="text-red-700">{tickets.error.message}</p>}
        {tickets.isSuccess && tickets.data.length === 0 && (
          <p className="text-stone-500">Henüz fiş yok. Sipariş gönderildiğinde veya hesap fişi yazdırıldığında burada görünecek.</p>
        )}

        <ul className="flex flex-wrap items-start gap-4">
          {tickets.data?.map((t) => (
            <li key={t.id}>
              <p className="mb-1 text-xs text-stone-500">
                {ticketKindLabels[t.kind]} · {t.title} · {formatTime(t.printedAt)}
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

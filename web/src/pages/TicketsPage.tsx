import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { PrintedTicket, PrintResult, PrintStatus } from '../api/types'
import { HomeBackLink } from '../components/BackLink'
import { TopBar } from '../components/TopBar'
import { formatTime } from '../lib/format'

/**
 * Fişler: son mutfak, hesap ve rapor fişleri, kâğıtta çıktıkları hâliyle. Yazıcı ayarlı değilse fişler yalnızca
 * burada görünür; yazıcıya ulaşılamadıysa kırmızı işaretlenir ve buradan tekrar yazdırılabilir.
 */
const ticketKindLabels: Record<PrintedTicket['kind'], string> = { Kitchen: 'Mutfak', Bill: 'Hesap', Report: 'Rapor' }

const statusBadges: Record<PrintStatus, { label: string; className: string }> = {
  Printed: { label: 'Yazdırıldı', className: 'bg-green-100 text-green-800' },
  Preview: { label: 'Yalnızca ekranda', className: 'bg-stone-300 text-stone-700' },
  Failed: { label: 'YAZDIRILAMADI', className: 'bg-red-600 text-white' },
}

export default function TicketsPage() {
  const queryClient = useQueryClient()
  const tickets = useQuery({
    queryKey: ['tickets'],
    queryFn: () => api<PrintedTicket[]>('GET', '/print/recent'),
  })

  const reprint = useMutation({
    mutationFn: (id: string) => api<PrintResult>('POST', `/print/${id}/reprint`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['tickets'] }),
  })

  const anyPrinted = tickets.data?.some((t) => t.status !== 'Preview')

  return (
    <div className="min-h-screen bg-stone-200">
      <TopBar
        title="Fişler"
        left={<HomeBackLink currentPath="/fisler" />}
      />

      <main className="mx-auto max-w-6xl p-4">
        {tickets.isSuccess && !anyPrinted && (
          <p className="mb-4 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
            Yazıcı ayarlı değilse fişler yalnızca burada, kâğıtta çıkacakları hâliyle gösterilir. Yazıcı adresi: Yönetim → Yazıcılar.
          </p>
        )}
        {reprint.isError && <p className="mb-4 rounded-xl bg-red-50 px-4 py-2 text-sm text-red-700" role="alert">{reprint.error.message}</p>}

        {tickets.isPending && <p className="text-stone-500">Yükleniyor…</p>}
        {tickets.isError && <p className="text-red-700">{tickets.error.message}</p>}
        {tickets.isSuccess && tickets.data.length === 0 && (
          <p className="text-stone-500">Henüz fiş yok. Sipariş gönderildiğinde veya hesap fişi yazdırıldığında burada görünecek.</p>
        )}

        <ul className="flex flex-wrap items-start gap-4">
          {tickets.data?.map((t) => {
            const badge = statusBadges[t.status]
            return (
              <li key={t.id}>
                <div className="mb-1 flex items-center gap-2 text-xs text-stone-500">
                  <span>
                    {ticketKindLabels[t.kind]} · {t.title} · {formatTime(t.printedAt)}
                  </span>
                  <span className={`rounded px-1.5 py-0.5 font-semibold ${badge.className}`}>{badge.label}</span>
                </div>
                {/* 48 karakterlik termal fiş: sabit genişlikli yazı ve beyaz kâğıt şeridi */}
                <pre
                  className={`w-fit overflow-x-auto bg-white px-3 py-4 font-mono text-xs leading-snug text-black shadow-md ${
                    t.status === 'Failed' ? 'ring-4 ring-red-500' : ''
                  }`}
                >
                  {t.text}
                </pre>
                {t.status === 'Failed' && t.error && <p className="mt-1 max-w-sm text-xs text-red-700">{t.error}</p>}
                {t.status !== 'Preview' && (
                  <button
                    type="button"
                    disabled={reprint.isPending}
                    onClick={() => reprint.mutate(t.id)}
                    className={`mt-2 rounded-lg px-3 py-1.5 text-sm font-semibold transition disabled:opacity-40 ${
                      t.status === 'Failed' ? 'bg-red-600 text-white hover:bg-red-700' : 'bg-white text-stone-800 ring-1 ring-stone-300 hover:bg-stone-50'
                    }`}
                  >
                    Tekrar yazdır
                  </button>
                )}
              </li>
            )
          })}
        </ul>
      </main>
    </div>
  )
}

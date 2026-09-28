import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { AdjustmentLine, ZReport } from '../api/types'
import { TopBar } from '../components/TopBar'
import { formatMoney, formatTime } from '../lib/format'

/**
 * Gün sonu (Z) raporu. İş günü sabah 05:00'te başlar; tarih seçilmezse bugünkü iş günü gösterilir.
 * Yalnızca işletme sahibi ve yönetici görebilir.
 */
export default function ReportPage() {
  // null: sunucu "bugünkü iş günü"nü kendisi seçsin (gece 02:00'de bile doğru günü verir).
  const [date, setDate] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const report = useQuery({
    queryKey: ['z-report', date],
    queryFn: () => api<ZReport>('GET', `/reports/z${date ? `?date=${date}` : ''}`),
  })

  const print = useMutation({
    mutationFn: () => api('POST', `/reports/z/print${date ? `?date=${date}` : ''}`),
    onSuccess: () => setNotice('Rapor yazdırıldı.'),
    onError: (err) => setNotice(err.message),
  })

  const z = report.data

  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar
        title="Gün sonu raporu"
        left={
          <Link to="/garson" className="rounded-xl bg-stone-100 px-3 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-200">
            ← Masalar
          </Link>
        }
      />

      <main className="mx-auto max-w-4xl space-y-4 p-4">
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-sm font-medium text-stone-700">
            İş günü
            <input
              type="date"
              value={date ?? z?.date ?? ''}
              onChange={(e) => {
                setNotice(null)
                setDate(e.target.value || null)
              }}
              className="mt-1 block rounded-xl border border-stone-300 bg-white px-3 py-2"
            />
          </label>
          <p className="pb-2 text-xs text-stone-500">
            {z && `${formatDateTime(z.from)} – ${formatDateTime(z.to)} arası (gün 05:00'te başlar)`}
          </p>
          <button
            type="button"
            disabled={!z || print.isPending}
            onClick={() => print.mutate()}
            className="ml-auto rounded-xl bg-stone-900 px-5 py-2.5 font-semibold text-white transition hover:bg-stone-700 disabled:opacity-40"
          >
            Yazdır
          </button>
        </div>

        {notice && <p className="rounded-xl bg-green-50 px-4 py-2 text-sm text-green-800 ring-1 ring-green-200">{notice}</p>}
        {report.isPending && <p className="text-stone-500">Rapor hazırlanıyor…</p>}
        {report.isError && <p className="text-red-700">{report.error.message}</p>}

        {z && (
          <>
            {z.openTableCount > 0 && (
              <p className="rounded-xl bg-orange-100 px-4 py-3 font-semibold text-orange-900 ring-1 ring-orange-300" role="alert">
                Dikkat: {z.openTableCount} masa hâlâ açık, ödenmemiş {formatMoney(z.openTablesTotal)} var. Günü kapatmadan önce ödemeleri alın.
              </p>
            )}

            <Section title="Tahsilat">
              <div className="grid grid-cols-3 gap-3">
                <Stat label="Nakit" value={formatMoney(z.cashTotal)} />
                <Stat label="Kart" value={formatMoney(z.cardTotal)} />
                <Stat label="Toplam" value={formatMoney(z.paymentsTotal)} strong />
              </div>
              <p className="mt-2 text-xs text-stone-500">Kasadaki nakdi "Nakit" rakamıyla karşılaştırın.</p>
            </Section>

            <Section title="Satış">
              <div className="grid grid-cols-3 gap-3">
                <Stat label="Kapanan adisyon" value={String(z.closedSessionCount)} />
                <Stat label="Satış toplamı" value={formatMoney(z.salesTotal)} />
                <Stat label="Ortalama hesap" value={formatMoney(z.averageBill)} />
              </div>
            </Section>

            <AdjustmentSection title="İptaller" lines={z.voids} total={z.voidsTotal} tone="red" />
            <AdjustmentSection title="İkramlar" lines={z.comps} total={z.compsTotal} tone="green" />

            <Section title="En çok satanlar">
              {z.topProducts.length === 0 ? (
                <p className="text-sm text-stone-500">Bu gün satış yok.</p>
              ) : (
                <ol className="divide-y divide-stone-100">
                  {z.topProducts.map((p, i) => (
                    <li key={p.productName} className="flex justify-between py-1.5 text-sm">
                      <span className="flex items-center gap-3">
                        {/* Sıra ayrı rozette; yoksa "1." ile "12 ×" yan yana "1.12" gibi okunuyor. */}
                        <span className="inline-flex size-6 items-center justify-center rounded-full bg-stone-100 text-xs font-semibold text-stone-500">
                          {i + 1}
                        </span>
                        <span>
                          <span className="font-semibold tabular-nums">{p.quantity} ×</span> {p.productName}
                        </span>
                      </span>
                      <span className="tabular-nums">{formatMoney(p.amount)}</span>
                    </li>
                  ))}
                </ol>
              )}
            </Section>
          </>
        )}
      </main>
    </div>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-stone-200">
      <h2 className="mb-3 text-sm font-semibold tracking-wide text-stone-500 uppercase">{title}</h2>
      {children}
    </section>
  )
}

function Stat({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={`rounded-xl p-3 ${strong ? 'bg-stone-900 text-white' : 'bg-stone-50'}`}>
      <p className={`text-xs ${strong ? 'text-stone-300' : 'text-stone-500'}`}>{label}</p>
      <p className="mt-1 text-xl font-bold tabular-nums">{value}</p>
    </div>
  )
}

function AdjustmentSection({ title, lines, total, tone }: { title: string; lines: AdjustmentLine[]; total: number; tone: 'red' | 'green' }) {
  return (
    <Section title={`${title} · ${lines.length} · ${formatMoney(total)}`}>
      {lines.length === 0 ? (
        <p className="text-sm text-stone-500">Yok.</p>
      ) : (
        <table className="w-full text-sm">
          <thead className="text-left text-xs text-stone-500">
            <tr>
              <th className="pb-1 font-medium">Saat</th>
              <th className="pb-1 font-medium">Masa</th>
              <th className="pb-1 font-medium">Ürün</th>
              <th className="pb-1 font-medium">Yapan</th>
              <th className="pb-1 text-right font-medium">Tutar</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-stone-100">
            {lines.map((l, i) => (
              <tr key={i}>
                <td className="py-1.5 tabular-nums">{formatTime(l.at)}</td>
                <td className="py-1.5">{l.tableName}</td>
                <td className="py-1.5">{l.quantity} × {l.productName}</td>
                <td className="py-1.5 text-stone-600">{l.byName ?? '—'}</td>
                <td className={`py-1.5 text-right tabular-nums ${tone === 'red' ? 'text-red-700' : 'text-green-700'}`}>{formatMoney(l.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Section>
  )
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
}

import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { KitchenOrder, OrderStatus } from '../api/types'
import { TopBar } from '../components/TopBar'
import { enableSound, isSoundEnabled, playBell } from '../lib/bell'
import { formatTime } from '../lib/format'
import { useRealtimeEvent } from '../realtime/useRealtime'

/**
 * Mutfak ekranı. Yeni siparişler anında (SignalR) düşer ve zil çalar.
 * Soldaki alanda hazırlanacaklar, sağda servis bekleyen hazır siparişler.
 */
export default function KitchenPage() {
  const queryClient = useQueryClient()
  const now = useNow(15_000)
  const [soundOn, setSoundOn] = useState(isSoundEnabled)
  const [error, setError] = useState<string | null>(null)

  const orders = useQuery({
    queryKey: ['kitchen-orders'],
    queryFn: () => api<KitchenOrder[]>('GET', '/kitchen/orders'),
  })

  useRealtimeEvent('OrderCreated', () => playBell())

  const changeStatus = useMutation({
    mutationFn: ({ id, status }: { id: string; status: OrderStatus }) =>
      api<KitchenOrder>('POST', `/orders/${id}/status`, { status }),
    onSuccess: () => {
      setError(null)
      queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] })
    },
    onError: (err) => {
      setError(err.message)
      queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] })
    },
  })

  const toCook = orders.data?.filter((o) => o.status === 'New' || o.status === 'Preparing') ?? []
  const ready = orders.data?.filter((o) => o.status === 'Ready') ?? []
  const move = (id: string, status: OrderStatus) => changeStatus.mutate({ id, status })

  return (
    <div className="min-h-screen bg-stone-900">
      <TopBar title="Mutfak" />

      {!soundOn && (
        <button
          type="button"
          onClick={() => {
            enableSound()
            setSoundOn(true)
          }}
          className="block w-full bg-amber-500 px-4 py-3 text-center font-semibold text-stone-900"
        >
          🔔 Yeni siparişte zil çalması için dokunun
        </button>
      )}
      {error && <p className="bg-red-700 px-4 py-2 text-center text-white" role="alert">{error}</p>}

      <div className="flex flex-col gap-6 p-4 xl:flex-row">
        <section className="flex-1">
          <h2 className="mb-3 text-sm font-semibold tracking-wide text-stone-400 uppercase">
            Hazırlanacak · {toCook.length}
          </h2>
          {orders.isPending && <p className="text-stone-400">Yükleniyor…</p>}
          {orders.isSuccess && toCook.length === 0 && (
            <p className="rounded-2xl border border-dashed border-stone-700 p-10 text-center text-stone-500">
              Bekleyen sipariş yok.
            </p>
          )}
          <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {toCook.map((o) => (
              <li key={o.id}>
                <OrderCard order={o} now={now}>
                  {o.status === 'New' ? (
                    <div className="flex gap-2">
                      <ActionButton color="amber" onClick={() => move(o.id, 'Preparing')}>Hazırlamaya başla</ActionButton>
                      <ActionButton color="green" onClick={() => move(o.id, 'Ready')} narrow>Hazır</ActionButton>
                    </div>
                  ) : (
                    <ActionButton color="green" onClick={() => move(o.id, 'Ready')}>Hazır</ActionButton>
                  )}
                </OrderCard>
              </li>
            ))}
          </ul>
        </section>

        <section className="xl:w-80">
          <h2 className="mb-3 text-sm font-semibold tracking-wide text-stone-400 uppercase">
            Hazır · servis bekliyor · {ready.length}
          </h2>
          <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-1">
            {ready.map((o) => (
              <li key={o.id} className="rounded-2xl bg-green-900/60 p-4 text-white ring-1 ring-green-700">
                <div className="flex items-baseline justify-between">
                  <span className="text-xl font-bold">{o.tableName}</span>
                  <span className="text-sm text-green-200">{formatTime(o.createdAt)}</span>
                </div>
                <p className="mt-1 text-sm text-green-100">
                  {o.items.map((i) => `${i.quantity}× ${i.productName}`).join(', ')}
                </p>
                <button
                  type="button"
                  onClick={() => move(o.id, 'Served')}
                  className="mt-3 w-full rounded-xl bg-white/10 py-2 text-sm font-semibold ring-1 ring-white/30 hover:bg-white/20"
                >
                  Servis edildi
                </button>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  )
}

function OrderCard({ order, now, children }: { order: KitchenOrder; now: number; children: React.ReactNode }) {
  // Ekrandaki "şimdi" 15 sn'de bir güncellenir; az önce gelen sipariş ondan yeni olabilir, eksiye düşmesin.
  const minutes = Math.max(0, Math.floor((now - new Date(order.createdAt).getTime()) / 60_000))
  // 10 dk'yı geçen sarı, 15 dk'yı geçen kırmızı: gecikenler bir bakışta görünsün.
  const timeColor = minutes >= 15 ? 'bg-red-600 text-white' : minutes >= 10 ? 'bg-amber-400 text-stone-900' : 'bg-stone-200 text-stone-700'
  const isNew = order.status === 'New'

  return (
    <article className={`flex h-full flex-col rounded-2xl bg-white p-4 shadow-lg ${isNew ? 'ring-4 ring-sky-400' : 'ring-4 ring-amber-400'}`}>
      <header className="flex items-start justify-between gap-2">
        <div>
          <h3 className="text-2xl font-bold text-stone-900">{order.tableName}</h3>
          <p className="text-sm text-stone-500">
            {formatTime(order.createdAt)}
            {order.createdByName && ` · ${order.createdByName}`}
          </p>
        </div>
        <span className={`rounded-lg px-2 py-1 text-sm font-bold tabular-nums ${timeColor}`}>{minutes} dk</span>
      </header>

      <p className={`mt-2 w-fit rounded-md px-2 py-0.5 text-xs font-semibold ${isNew ? 'bg-sky-100 text-sky-800' : 'bg-amber-100 text-amber-800'}`}>
        {isNew ? 'YENİ' : 'HAZIRLANIYOR'}
      </p>

      <ul className="mt-3 flex-1 space-y-2">
        {order.items.map((i) => (
          <li key={i.id}>
            <p className="text-lg font-semibold text-stone-900">
              <span className="tabular-nums">{i.quantity}×</span> {i.productName}
            </p>
            {i.note && <p className="ml-7 rounded bg-amber-100 px-2 py-0.5 text-sm font-semibold text-amber-900">⚠ {i.note}</p>}
          </li>
        ))}
      </ul>

      <div className="mt-4">{children}</div>
    </article>
  )
}

function ActionButton({ color, onClick, narrow = false, children }: { color: 'amber' | 'green'; onClick: () => void; narrow?: boolean; children: string }) {
  const colors = color === 'amber' ? 'bg-amber-600 hover:bg-amber-700' : 'bg-green-700 hover:bg-green-800'
  return (
    <button
      type="button"
      onClick={onClick}
      className={`${narrow ? 'px-4' : 'flex-1 w-full'} rounded-xl py-3 text-lg font-semibold text-white transition active:scale-[0.98] ${colors}`}
    >
      {children}
    </button>
  )
}

/** Belirli aralıklarla güncellenen "şimdi"; kartlardaki dakika sayaçları için. */
function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}

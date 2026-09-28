import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { KitchenOrder } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { playChime } from '../lib/bell'
import { useRealtimeEvent } from './useRealtime'

const SHOW_FOR_MS = 10_000

/**
 * Salon ekibine (garson, kasa, yönetici) "Masa 3: sipariş hazır" bildirimi.
 * Mutfak siparişi "Hazır" yaptığında ekranın köşesinde çıkar; dokununca o masaya gider.
 */
export function ReadyNotifications() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [ready, setReady] = useState<KitchenOrder[]>([])

  const isFrontOfHouse = user != null && user.role !== 'Kitchen'

  useRealtimeEvent('OrderUpdated', (order) => {
    if (!isFrontOfHouse || order.status !== 'Ready') return
    setReady((list) => [...list.filter((o) => o.id !== order.id), order])
    playChime()
    setTimeout(() => setReady((list) => list.filter((o) => o.id !== order.id)), SHOW_FOR_MS)
  })

  if (ready.length === 0) return null

  return (
    <div className="fixed right-4 bottom-4 z-50 flex w-72 flex-col gap-2" role="status" aria-live="polite">
      {ready.map((order) => (
        <button
          key={order.id}
          type="button"
          onClick={() => {
            setReady((list) => list.filter((o) => o.id !== order.id))
            navigate(`/garson/masa/${order.tableId}`)
          }}
          className="rounded-2xl bg-green-700 p-4 text-left text-white shadow-lg transition active:scale-[0.98]"
        >
          <p className="text-lg font-bold">{order.tableName}: sipariş hazır</p>
          <p className="mt-1 truncate text-sm text-green-100">
            {order.items.map((i) => `${i.quantity}× ${i.productName}`).join(', ')}
          </p>
        </button>
      ))}
    </div>
  )
}

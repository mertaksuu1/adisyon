import type { OrderStatus } from '../api/types'

export const orderStatusLabels: Record<OrderStatus, string> = {
  New: 'Yeni',
  Preparing: 'Hazırlanıyor',
  Ready: 'Hazır',
  Served: 'Servis edildi',
  Cancelled: 'İptal',
}

/** Durum rozeti renkleri (açık zemin üzerinde okunaklı). */
export const orderStatusBadge: Record<OrderStatus, string> = {
  New: 'bg-sky-100 text-sky-800',
  Preparing: 'bg-amber-100 text-amber-800',
  Ready: 'bg-green-100 text-green-800',
  Served: 'bg-stone-100 text-stone-600',
  Cancelled: 'bg-red-100 text-red-700',
}

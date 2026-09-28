import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import type { OrderItem, Session } from '../api/types'
import { formatMoney } from '../lib/format'

type Props = {
  session: Session
  item: OrderItem
  onClose: () => void
  onDone: (session: Session, message: string) => void
  onError: (message: string) => void
}

/** Adisyondaki bir ürün için iptal/ikram penceresi. Adet seçilebilir ("2 kebaptan 1'i"). */
export function ItemActionDialog({ session, item, onClose, onDone, onError }: Props) {
  const [quantity, setQuantity] = useState(item.quantity)

  const adjust = useMutation({
    mutationFn: (action: 'void' | 'comp') =>
      api<Session>('POST', `/sessions/${session.id}/items/${item.id}/${action}`, { quantity, version: session.version }),
    onSuccess: (updated, action) =>
      onDone(updated, action === 'void'
        ? `${quantity} x ${item.productName} iptal edildi, mutfağa iptal fişi gönderildi.`
        : `${quantity} x ${item.productName} ikram edildi.`),
    onError: (err) => onError(err.message),
  })

  return (
    <div className="fixed inset-0 z-30 flex items-end justify-center bg-black/40 p-4 sm:items-center" role="dialog" aria-modal="true" aria-labelledby="item-title">
      <div className="w-full max-w-sm rounded-3xl bg-white p-6 shadow-xl">
        <h2 id="item-title" className="text-xl font-bold text-stone-900">{item.productName}</h2>
        <p className="mt-1 text-sm text-stone-500">
          {item.quantity} adet · {formatMoney(item.unitPrice)} / adet
        </p>

        {item.quantity > 1 && (
          <div className="mt-5 flex items-center justify-center gap-4">
            <StepButton label="Azalt" onClick={() => setQuantity((q) => Math.max(1, q - 1))}>−</StepButton>
            <span className="w-24 text-center text-2xl font-bold tabular-nums">{quantity} adet</span>
            <StepButton label="Artır" onClick={() => setQuantity((q) => Math.min(item.quantity, q + 1))}>+</StepButton>
          </div>
        )}

        <div className="mt-6 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={adjust.isPending}
            onClick={() => adjust.mutate('void')}
            className="rounded-xl bg-red-700 py-4 text-lg font-semibold text-white transition hover:bg-red-800 disabled:opacity-40"
          >
            İptal et
          </button>
          <button
            type="button"
            disabled={adjust.isPending}
            onClick={() => adjust.mutate('comp')}
            className="rounded-xl bg-green-700 py-4 text-lg font-semibold text-white transition hover:bg-green-800 disabled:opacity-40"
          >
            İkram et
          </button>
        </div>
        <p className="mt-3 text-center text-xs text-stone-500">İptal mutfağa fiş olarak bildirilir ve kimin yaptığı kaydedilir.</p>
        <button type="button" onClick={onClose} className="mt-2 w-full py-2 text-sm font-medium text-stone-500 hover:text-stone-800">
          Vazgeç
        </button>
      </div>
    </div>
  )
}

function StepButton({ label, onClick, children }: { label: string; onClick: () => void; children: string }) {
  return (
    <button type="button" aria-label={label} onClick={onClick} className="size-12 rounded-xl bg-stone-100 text-2xl font-semibold text-stone-700 active:scale-95">
      {children}
    </button>
  )
}

import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import type { PaymentMethod, Session } from '../api/types'
import { formatMoney, parseAmount } from '../lib/format'

type Props = {
  session: Session
  onClose: () => void
  /** Ödeme alındıktan sonra; güncel adisyonla çağrılır (kapandıysa status 'Closed'). */
  onPaid: (session: Session) => void
  /** Ödeme reddedildi (ör. adisyon bu arada değişti). Pencere kapanır, mesaj masa ekranında gösterilir. */
  onError: (message: string) => void
}

/**
 * Ödeme penceresi. Varsayılan tutar kalanın tamamıdır: kasiyer tek dokunuşla "Nakit" veya "Kart" der.
 * Hesap bölünecekse tutar değiştirilir. Nakitte "Alınan" girilirse para üstü hesaplanır (yalnızca ekranda).
 */
export function PaymentDialog({ session, onClose, onPaid, onError }: Props) {
  const [amountText, setAmountText] = useState(toInput(session.remaining))
  const [receivedText, setReceivedText] = useState('')

  const amount = parseAmount(amountText)
  const received = parseAmount(receivedText)
  const change = received !== null && amount !== null ? received - amount : null
  const amountValid = amount !== null && amount > 0 && amount <= session.remaining

  const pay = useMutation({
    mutationFn: (method: PaymentMethod) =>
      api<Session>('POST', `/sessions/${session.id}/payments`, {
        method,
        version: session.version,
        // Kalanın tamamıysa tutar göndermiyoruz; sunucu kalanı kendisi hesaplar (kuruş farkı olmaz).
        amount: amount === session.remaining ? null : amount,
      }),
    onSuccess: onPaid,
    onError: (err) => onError(err.message),
  })

  return (
    <div className="fixed inset-0 z-30 flex items-end justify-center bg-black/40 p-4 sm:items-center" role="dialog" aria-modal="true" aria-labelledby="pay-title">
      <div className="w-full max-w-sm rounded-3xl bg-white p-6 shadow-xl">
        <h2 id="pay-title" className="text-xl font-bold text-stone-900">
          {session.tableName} · Ödeme
        </h2>

        <dl className="mt-4 space-y-1 text-sm">
          <Row label="Toplam" value={formatMoney(session.total)} />
          {session.paid > 0 && <Row label="Ödenen" value={formatMoney(session.paid)} />}
          <Row label="Kalan" value={formatMoney(session.remaining)} strong />
        </dl>

        <label htmlFor="amount" className="mt-5 block text-sm font-medium text-stone-700">
          Ödenecek tutar
        </label>
        <input
          id="amount"
          inputMode="decimal"
          value={amountText}
          onChange={(e) => setAmountText(e.target.value)}
          onFocus={(e) => e.target.select()}
          className="mt-1 w-full rounded-xl border border-stone-300 px-4 py-3 text-right text-2xl font-semibold tabular-nums focus:border-amber-600 focus:ring-2 focus:ring-amber-200 focus:outline-none"
        />
        <p className="mt-1 text-xs text-stone-500">Hesap bölünecekse tutarı değiştirin.</p>

        <label htmlFor="received" className="mt-4 block text-sm font-medium text-stone-700">
          Nakit alınan <span className="font-normal text-stone-400">(para üstü için, isteğe bağlı)</span>
        </label>
        <input
          id="received"
          inputMode="decimal"
          value={receivedText}
          onChange={(e) => setReceivedText(e.target.value)}
          placeholder="ör. 1000"
          className="mt-1 w-full rounded-xl border border-stone-300 px-4 py-2 text-right tabular-nums focus:border-amber-600 focus:outline-none"
        />
        {change !== null && (
          <p className={`mt-2 text-right text-lg font-bold tabular-nums ${change >= 0 ? 'text-green-700' : 'text-red-700'}`}>
            {change >= 0 ? `Para üstü: ${formatMoney(change)}` : `Eksik: ${formatMoney(-change)}`}
          </p>
        )}

        {!amountValid && amountText !== '' && (
          <p className="mt-2 text-sm text-red-700">Tutar 0'dan büyük ve kalan tutardan küçük ya da eşit olmalı.</p>
        )}

        <div className="mt-5 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={!amountValid || pay.isPending}
            onClick={() => pay.mutate('Cash')}
            className="rounded-xl bg-green-700 py-4 text-lg font-semibold text-white transition hover:bg-green-800 disabled:opacity-40"
          >
            Nakit
          </button>
          <button
            type="button"
            disabled={!amountValid || pay.isPending}
            onClick={() => pay.mutate('Card')}
            className="rounded-xl bg-sky-700 py-4 text-lg font-semibold text-white transition hover:bg-sky-800 disabled:opacity-40"
          >
            Kart
          </button>
        </div>
        <button type="button" onClick={onClose} className="mt-3 w-full py-2 text-sm font-medium text-stone-500 hover:text-stone-800">
          Vazgeç
        </button>
      </div>
    </div>
  )
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={`flex justify-between ${strong ? 'text-lg font-bold text-stone-900' : 'text-stone-600'}`}>
      <dt>{label}</dt>
      <dd className="tabular-nums">{value}</dd>
    </div>
  )
}

/** 460 → "460,00" (Türkçe klavyede virgül kullanılır). */
function toInput(value: number) {
  return value.toFixed(2).replace('.', ',')
}

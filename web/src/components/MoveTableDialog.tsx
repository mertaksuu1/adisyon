import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Session, Table } from '../api/types'
import { formatMoney } from '../lib/format'

type Props = {
  session: Session
  tables: Table[]
  onClose: () => void
  onMoved: (session: Session, message: string) => void
  onError: (message: string) => void
}

/**
 * Masa taşıma/birleştirme. Boş masa seçilirse adisyon oraya taşınır; dolu masa seçilirse onayla birleştirilir.
 */
export function MoveTableDialog({ session, tables, onClose, onMoved, onError }: Props) {
  const move = useMutation({
    mutationFn: (target: Table) =>
      api<Session>('POST', `/sessions/${session.id}/move`, { targetTableId: target.id, version: session.version }),
    onSuccess: (moved, target) =>
      onMoved(moved, target.openSession
        ? `${session.tableName} ile ${target.name} birleştirildi.`
        : `Adisyon ${session.tableName} → ${target.name} taşındı.`),
    onError: (err) => onError(err.message),
  })

  const choices = tables.filter((t) => t.isActive && t.id !== session.tableId)

  function choose(target: Table) {
    if (target.openSession && !confirm(`${session.tableName} ile ${target.name} hesapları tek adisyonda birleştirilsin mi?`)) {
      return
    }
    move.mutate(target)
  }

  return (
    <div className="fixed inset-0 z-30 flex items-end justify-center bg-black/40 p-4 sm:items-center" role="dialog" aria-modal="true" aria-labelledby="move-title">
      <div className="w-full max-w-lg rounded-3xl bg-white p-6 shadow-xl">
        <h2 id="move-title" className="text-xl font-bold text-stone-900">{session.tableName} · Masa taşı</h2>
        <p className="mt-1 text-sm text-stone-500">Boş masaya taşınır; dolu masa seçerseniz iki hesap birleşir.</p>

        <ul className="mt-4 grid max-h-80 grid-cols-3 gap-2 overflow-y-auto">
          {choices.map((t) => (
            <li key={t.id}>
              <button
                type="button"
                disabled={move.isPending}
                onClick={() => choose(t)}
                className={`flex h-20 w-full flex-col justify-between rounded-xl p-2 text-left transition active:scale-[0.98] disabled:opacity-50 ${
                  t.openSession ? 'bg-amber-700 text-white' : 'bg-stone-50 text-stone-900 ring-1 ring-stone-200 hover:ring-amber-400'
                }`}
              >
                <span className="font-semibold">{t.name}</span>
                <span className={`text-xs ${t.openSession ? 'text-amber-100' : 'text-stone-400'}`}>
                  {t.openSession ? `${formatMoney(t.openSession.total)} · birleştir` : 'Boş'}
                </span>
              </button>
            </li>
          ))}
        </ul>

        <button type="button" onClick={onClose} className="mt-4 w-full py-2 text-sm font-medium text-stone-500 hover:text-stone-800">
          Vazgeç
        </button>
      </div>
    </div>
  )
}

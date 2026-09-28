import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { api, ApiError } from '../api/client'
import type { Category, NewOrderItem, Product, Session, Table } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { canCheckout } from '../auth/roles'
import { TopBar } from '../components/TopBar'
import { formatMoney, formatTime } from '../lib/format'

/** Sepette henüz mutfağa gönderilmemiş bir satır. */
type CartLine = { key: string; product: Product; quantity: number; note: string }

/**
 * Bir masanın sipariş ekranı. Solda menü, sağda adisyon + yeni sepet.
 * Boş masada adisyon, ilk sipariş gönderilirken açılır (yanlışlıkla dokunup çıkınca boş adisyon kalmasın).
 */
export default function TableOrderPage() {
  const { tableId } = useParams<{ tableId: string }>()
  const { user } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const tables = useQuery({ queryKey: ['tables'], queryFn: () => api<Table[]>('GET', '/tables') })
  const menu = useQuery({ queryKey: ['menu'], queryFn: () => api<Category[]>('GET', '/menu') })
  const table = tables.data?.find((t) => t.id === tableId)
  const sessionId = table?.openSession?.sessionId
  const session = useQuery({
    queryKey: ['session', sessionId],
    queryFn: () => api<Session>('GET', `/sessions/${sessionId}`),
    enabled: !!sessionId,
  })

  const categories = useMemo(
    () => (menu.data ?? []).filter((c) => c.isActive && c.products.some((p) => p.isActive)),
    [menu.data],
  )
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null)
  const currentCategory = categories.find((c) => c.id === selectedCategoryId) ?? categories[0]

  const [cart, setCart] = useState<CartLine[]>([])
  const [editingNote, setEditingNote] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const cartTotal = cart.reduce((sum, l) => sum + l.product.price * l.quantity, 0)

  function addToCart(product: Product) {
    setCart((lines) => {
      // Notsuz aynı ürün varsa adedini artır; notlu satırlar ayrı kalır ("1 acılı, 1 acısız").
      const existing = lines.find((l) => l.product.id === product.id && l.note === '')
      if (existing) return lines.map((l) => (l === existing ? { ...l, quantity: l.quantity + 1 } : l))
      return [...lines, { key: crypto.randomUUID(), product, quantity: 1, note: '' }]
    })
  }

  function changeQuantity(key: string, delta: number) {
    setCart((lines) =>
      lines.flatMap((l) => {
        if (l.key !== key) return [l]
        const quantity = l.quantity + delta
        return quantity <= 0 ? [] : [{ ...l, quantity: Math.min(quantity, 99) }]
      }),
    )
  }

  function setNote(key: string, note: string) {
    setCart((lines) => lines.map((l) => (l.key === key ? { ...l, note } : l)))
  }

  const sendOrder = useMutation({
    mutationFn: async () => {
      let id = sessionId
      if (!id) {
        try {
          id = (await api<Session>('POST', `/tables/${tableId}/session`)).id
        } catch (err) {
          // Başka bir garson masayı az önce açtıysa onun adisyonuna ekle.
          if (err instanceof ApiError && err.status === 409 && typeof err.extensions.sessionId === 'string') {
            id = err.extensions.sessionId
          } else {
            throw err
          }
        }
      }
      const items: NewOrderItem[] = cart.map((l) => ({
        productId: l.product.id,
        quantity: l.quantity,
        note: l.note.trim() || null,
      }))
      return api<Session>('POST', `/sessions/${id}/orders`, { items })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables'] })
      // Siparişten sonra masa planına dön; sıradaki masaya geçmek en sık yapılan iş.
      navigate('/garson', { state: { flash: `${table?.name}: sipariş gönderildi, mutfak fişi yazdırıldı.` } })
    },
    onError: (err) => setError(err.message),
  })

  const closeBill = useMutation({
    mutationFn: (s: Session) => api<Session>('POST', `/sessions/${s.id}/close`, { version: s.version }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tables'] })
      navigate('/garson', { state: { flash: `${table?.name}: hesap kapatıldı.` } })
    },
    onError: (err) => {
      setError(err.message)
      // Sürüm çakışması: güncel adisyonu yeniden yükle ki kasiyer yeni tutarı görsün.
      queryClient.invalidateQueries({ queryKey: ['session', sessionId] })
      queryClient.invalidateQueries({ queryKey: ['tables'] })
    },
  })

  if (tables.isSuccess && !table) {
    return <p className="p-8 text-stone-600">Masa bulunamadı. <Link to="/garson" className="text-amber-700 underline">Masalara dön</Link></p>
  }

  return (
    <div className="flex min-h-screen flex-col bg-stone-100">
      <TopBar
        title={table?.name ?? '…'}
        left={
          <Link to="/garson" className="rounded-xl bg-stone-100 px-3 py-2.5 text-sm font-semibold text-stone-700 hover:bg-stone-200">
            ← Masalar
          </Link>
        }
      />

      <div className="flex flex-1 flex-col lg:flex-row">
        {/* --- Menü --- */}
        <section className="flex-1 p-4">
          <nav className="flex gap-2 overflow-x-auto pb-3" aria-label="Kategoriler">
            {categories.map((c) => (
              <button
                key={c.id}
                type="button"
                onClick={() => setSelectedCategoryId(c.id)}
                className={`shrink-0 rounded-full px-4 py-2 font-medium transition ${
                  c.id === currentCategory?.id ? 'bg-stone-900 text-white' : 'bg-white text-stone-700 ring-1 ring-stone-200'
                }`}
              >
                {c.name}
              </button>
            ))}
          </nav>

          {menu.isPending && <p className="text-stone-500">Menü yükleniyor…</p>}
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            {currentCategory?.products
              .filter((p) => p.isActive)
              .map((p) => (
                <li key={p.id}>
                  <button
                    type="button"
                    onClick={() => addToCart(p)}
                    className="flex h-28 w-full flex-col justify-between rounded-2xl bg-white p-3 text-left shadow-sm ring-1 ring-stone-200 transition hover:ring-amber-400 active:scale-[0.98]"
                  >
                    <span className="line-clamp-2 font-semibold text-stone-900">{p.name}</span>
                    <span className="font-medium text-amber-700 tabular-nums">{formatMoney(p.price)}</span>
                  </button>
                </li>
              ))}
          </ul>
        </section>

        {/* --- Adisyon ve sepet --- */}
        <aside className="flex flex-col border-t border-stone-200 bg-white lg:w-96 lg:border-t-0 lg:border-l">
          <div className="flex-1 overflow-y-auto p-4">
            {session.data && session.data.orders.length > 0 && (
              <section className="mb-5">
                <h2 className="text-sm font-semibold tracking-wide text-stone-500 uppercase">Adisyon</h2>
                <ul className="mt-2 divide-y divide-stone-100">
                  {session.data.orders.map((o) => (
                    <li key={o.id} className="py-2">
                      <p className="text-xs text-stone-400">{formatTime(o.createdAt)}</p>
                      {o.items.map((i) => (
                        <div key={i.id} className="flex justify-between gap-2 text-sm">
                          <span className="text-stone-700">
                            {i.quantity} × {i.productName}
                            {i.note && <span className="block text-xs text-amber-700">{i.note}</span>}
                          </span>
                          <span className="text-stone-600 tabular-nums">{formatMoney(i.unitPrice * i.quantity)}</span>
                        </div>
                      ))}
                    </li>
                  ))}
                </ul>
                <p className="mt-2 flex justify-between border-t border-stone-200 pt-2 font-semibold">
                  <span>Adisyon toplamı</span>
                  <span className="tabular-nums">{formatMoney(session.data.total)}</span>
                </p>
              </section>
            )}

            <h2 className="text-sm font-semibold tracking-wide text-stone-500 uppercase">Yeni sipariş</h2>
            {cart.length === 0 ? (
              <p className="mt-2 text-sm text-stone-400">Menüden ürün seçin.</p>
            ) : (
              <ul className="mt-2 space-y-2">
                {cart.map((l) => (
                  <li key={l.key} className="rounded-xl bg-stone-50 p-2">
                    <div className="flex items-center gap-2">
                      <span className="flex-1 font-medium text-stone-900">{l.product.name}</span>
                      <QtyButton label="Azalt" onClick={() => changeQuantity(l.key, -1)}>−</QtyButton>
                      <span className="w-6 text-center font-semibold tabular-nums">{l.quantity}</span>
                      <QtyButton label="Artır" onClick={() => changeQuantity(l.key, 1)}>+</QtyButton>
                    </div>
                    {editingNote === l.key ? (
                      <input
                        autoFocus
                        value={l.note}
                        onChange={(e) => setNote(l.key, e.target.value)}
                        onBlur={() => setEditingNote(null)}
                        onKeyDown={(e) => e.key === 'Enter' && setEditingNote(null)}
                        placeholder="ör. soğansız, az pişmiş"
                        maxLength={500}
                        className="mt-2 w-full rounded-lg border border-stone-300 px-2 py-1.5 text-sm focus:border-amber-600 focus:outline-none"
                      />
                    ) : (
                      <button type="button" onClick={() => setEditingNote(l.key)} className="mt-1 text-xs text-amber-700">
                        {l.note ? `Not: ${l.note}` : '+ Not ekle'}
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            )}

            {error && <p className="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</p>}
          </div>

          <div className="sticky bottom-0 space-y-2 border-t border-stone-200 bg-white p-4">
            <button
              type="button"
              disabled={cart.length === 0 || sendOrder.isPending}
              onClick={() => {
                setError(null)
                sendOrder.mutate()
              }}
              className="flex w-full items-center justify-between rounded-xl bg-amber-600 px-5 py-4 text-lg font-semibold text-white transition hover:bg-amber-700 disabled:bg-stone-300"
            >
              <span>{sendOrder.isPending ? 'Gönderiliyor…' : 'Mutfağa gönder'}</span>
              <span className="tabular-nums">{formatMoney(cartTotal)}</span>
            </button>

            {user && canCheckout(user.role) && session.data && cart.length === 0 && (
              <button
                type="button"
                disabled={closeBill.isPending}
                onClick={() => {
                  const s = session.data
                  if (confirm(`${table?.name} hesabı ${formatMoney(s.total)} olarak kapatılsın mı?`)) {
                    setError(null)
                    closeBill.mutate(s)
                  }
                }}
                className="w-full rounded-xl bg-stone-900 py-3 font-semibold text-white transition hover:bg-stone-700 disabled:opacity-50"
              >
                Hesabı kapat · {formatMoney(session.data.total)}
              </button>
            )}
          </div>
        </aside>
      </div>
    </div>
  )
}

function QtyButton({ label, onClick, children }: { label: string; onClick: () => void; children: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      className="size-9 rounded-lg bg-white text-lg font-semibold text-stone-700 ring-1 ring-stone-200 active:scale-95"
    >
      {children}
    </button>
  )
}

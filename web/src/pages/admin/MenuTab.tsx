import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Category, Product } from '../../api/types'
import { formatMoney, parseAmount } from '../../lib/format'
import { Badge, Button, Card, Input, Message } from './ui'

/**
 * Menü yönetimi: kategori ve ürün ekleme, isim/fiyat düzenleme, pasife alma.
 * Silme yok: geçmiş adisyonlar ürünlere bağlı; menüden kaldırmak için "Pasife al".
 */
export function MenuTab() {
  const queryClient = useQueryClient()
  const menu = useQuery({ queryKey: ['menu'], queryFn: () => api<Category[]>('GET', '/menu') })
  const [error, setError] = useState<string | null>(null)
  const [newCategory, setNewCategory] = useState('')

  const refresh = () => {
    setError(null)
    return queryClient.invalidateQueries({ queryKey: ['menu'] })
  }
  const onError = (err: Error) => setError(err.message)

  const addCategory = useMutation({
    mutationFn: (name: string) => api('POST', '/categories', { name }),
    onSuccess: () => {
      setNewCategory('')
      return refresh()
    },
    onError,
  })
  const saveCategory = useMutation({
    mutationFn: (c: Pick<Category, 'id' | 'name' | 'isActive'>) => api('PUT', `/categories/${c.id}`, { name: c.name, isActive: c.isActive }),
    onSuccess: refresh,
    onError,
  })
  const saveProduct = useMutation({
    mutationFn: (p: Omit<Product, 'id' | 'sortOrder'> & { id?: string }) =>
      p.id
        ? api('PUT', `/products/${p.id}`, p)
        : api('POST', '/products', p),
    onSuccess: refresh,
    onError,
  })

  function submitCategory(e: FormEvent) {
    e.preventDefault()
    if (newCategory.trim()) addCategory.mutate(newCategory.trim())
  }

  return (
    <div className="space-y-4">
      <Message error={error} />

      <form onSubmit={submitCategory} className="flex gap-2">
        <Input value={newCategory} onChange={(e) => setNewCategory(e.target.value)} placeholder="Yeni kategori adı, ör. Kahvaltı" maxLength={100} className="flex-1" />
        <Button type="submit" variant="primary" disabled={!newCategory.trim() || addCategory.isPending}>
          Kategori ekle
        </Button>
      </form>

      {menu.isPending && <p className="text-stone-500">Menü yükleniyor…</p>}
      {menu.data?.map((category) => (
        <CategoryCard
          key={category.id}
          category={category}
          onSaveCategory={(c) => saveCategory.mutate(c)}
          onSaveProduct={(p) => saveProduct.mutate(p)}
          busy={saveProduct.isPending || saveCategory.isPending}
        />
      ))}
    </div>
  )
}

type SaveProduct = Omit<Product, 'id' | 'sortOrder'> & { id?: string }

function CategoryCard({
  category,
  onSaveCategory,
  onSaveProduct,
  busy,
}: {
  category: Category
  onSaveCategory: (c: Pick<Category, 'id' | 'name' | 'isActive'>) => void
  onSaveProduct: (p: SaveProduct) => void
  busy: boolean
}) {
  const [renaming, setRenaming] = useState(false)
  const [name, setName] = useState(category.name)

  return (
    <Card
      title={
        renaming ? (
          <form
            className="flex gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              onSaveCategory({ id: category.id, name, isActive: category.isActive })
              setRenaming(false)
            }}
          >
            <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus maxLength={100} />
            <Button type="submit" variant="primary">Kaydet</Button>
            <Button onClick={() => setRenaming(false)}>Vazgeç</Button>
          </form>
        ) : (
          <span className={category.isActive ? '' : 'text-stone-400'}>
            {category.name} {!category.isActive && <Badge tone="gray">Pasif</Badge>}
          </span>
        )
      }
      actions={
        !renaming && (
          <>
            <Button variant="link" onClick={() => setRenaming(true)}>Adını değiştir</Button>
            <Button
              variant={category.isActive ? 'danger' : 'secondary'}
              onClick={() => onSaveCategory({ id: category.id, name: category.name, isActive: !category.isActive })}
            >
              {category.isActive ? 'Kategoriyi pasife al' : 'Aktif et'}
            </Button>
          </>
        )
      }
    >
      <ul className="divide-y divide-stone-100">
        {category.products.map((p) => (
          <ProductRow key={p.id} product={p} onSave={onSaveProduct} busy={busy} />
        ))}
      </ul>
      <NewProductRow categoryId={category.id} onSave={onSaveProduct} busy={busy} />
    </Card>
  )
}

function ProductRow({ product, onSave, busy }: { product: Product; onSave: (p: SaveProduct) => void; busy: boolean }) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(product.name)
  const [price, setPrice] = useState(product.price.toFixed(2).replace('.', ','))
  const parsedPrice = parseAmount(price)

  if (editing) {
    return (
      <li className="py-2">
        <form
          className="flex flex-wrap gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (parsedPrice === null) return
            onSave({ ...product, name, price: parsedPrice })
            setEditing(false)
          }}
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={200} className="min-w-40 flex-1" autoFocus />
          <Input value={price} onChange={(e) => setPrice(e.target.value)} inputMode="decimal" className="w-28 text-right" aria-label="Fiyat" />
          <Button type="submit" variant="primary" disabled={parsedPrice === null || parsedPrice < 0 || !name.trim()}>Kaydet</Button>
          <Button onClick={() => setEditing(false)}>Vazgeç</Button>
        </form>
        <p className="mt-1 text-xs text-stone-500">Fiyat değişikliği yalnızca yeni siparişlere uygulanır; açık adisyonlar eski fiyatla kalır.</p>
      </li>
    )
  }

  return (
    <li className={`flex flex-wrap items-center gap-2 py-2 ${product.isActive ? '' : 'text-stone-400'}`}>
      <span className="flex-1">
        {product.name} {!product.isActive && <Badge tone="gray">Pasif</Badge>}
      </span>
      <span className="w-24 text-right tabular-nums">{formatMoney(product.price)}</span>
      <Button variant="link" onClick={() => setEditing(true)} disabled={busy}>Düzenle</Button>
      <Button
        variant={product.isActive ? 'danger' : 'secondary'}
        disabled={busy}
        onClick={() => onSave({ ...product, isActive: !product.isActive })}
      >
        {product.isActive ? 'Pasife al' : 'Aktif et'}
      </Button>
    </li>
  )
}

function NewProductRow({ categoryId, onSave, busy }: { categoryId: string; onSave: (p: SaveProduct) => void; busy: boolean }) {
  const [name, setName] = useState('')
  const [price, setPrice] = useState('')
  const parsedPrice = parseAmount(price)

  return (
    <form
      className="mt-2 flex flex-wrap gap-2 border-t border-dashed border-stone-200 pt-3"
      onSubmit={(e) => {
        e.preventDefault()
        if (parsedPrice === null) return
        onSave({ categoryId, name: name.trim(), price: parsedPrice, description: null, isActive: true })
        setName('')
        setPrice('')
      }}
    >
      <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Yeni ürün adı" maxLength={200} className="min-w-40 flex-1" />
      <Input value={price} onChange={(e) => setPrice(e.target.value)} placeholder="Fiyat" inputMode="decimal" className="w-28 text-right" aria-label="Yeni ürün fiyatı" />
      <Button type="submit" variant="primary" disabled={busy || !name.trim() || parsedPrice === null || parsedPrice < 0}>
        Ürün ekle
      </Button>
    </form>
  )
}

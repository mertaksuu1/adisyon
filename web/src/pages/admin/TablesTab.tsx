import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Table } from '../../api/types'
import { Badge, Button, Card, Input, Message } from './ui'

/** Masa yönetimi: ekleme, yeniden adlandırma, kullanım dışı bırakma (açık adisyonu olan masa bırakılamaz). */
export function TablesTab() {
  const queryClient = useQueryClient()
  const tables = useQuery({ queryKey: ['tables'], queryFn: () => api<Table[]>('GET', '/tables') })
  const [error, setError] = useState<string | null>(null)
  const [newName, setNewName] = useState('')

  const refresh = () => {
    setError(null)
    return queryClient.invalidateQueries({ queryKey: ['tables'] })
  }

  const add = useMutation({
    mutationFn: (name: string) => api('POST', '/tables', { name }),
    onSuccess: () => {
      setNewName('')
      return refresh()
    },
    onError: (err) => setError(err.message),
  })
  const save = useMutation({
    mutationFn: (t: Pick<Table, 'id' | 'name' | 'isActive'>) => api('PUT', `/tables/${t.id}`, { name: t.name, isActive: t.isActive }),
    onSuccess: refresh,
    onError: (err) => setError(err.message),
  })

  return (
    <div className="space-y-4">
      <Message error={error} />
      <Card
        title="Masalar"
        actions={
          <form
            className="flex gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              if (newName.trim()) add.mutate(newName.trim())
            }}
          >
            <Input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="ör. Bahçe 1" maxLength={100} />
            <Button type="submit" variant="primary" disabled={!newName.trim() || add.isPending}>Masa ekle</Button>
          </form>
        }
      >
        <ul className="divide-y divide-stone-100">
          {tables.data?.map((t) => (
            <TableRow key={t.id} table={t} busy={save.isPending} onSave={(x) => save.mutate(x)} />
          ))}
        </ul>
      </Card>
    </div>
  )
}

function TableRow({ table, busy, onSave }: { table: Table; busy: boolean; onSave: (t: Pick<Table, 'id' | 'name' | 'isActive'>) => void }) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(table.name)

  return (
    <li className={`flex flex-wrap items-center gap-2 py-2 ${table.isActive ? '' : 'text-stone-400'}`}>
      {editing ? (
        <form
          className="flex flex-1 gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            onSave({ id: table.id, name, isActive: table.isActive })
            setEditing(false)
          }}
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus maxLength={100} className="flex-1" />
          <Button type="submit" variant="primary" disabled={!name.trim()}>Kaydet</Button>
          <Button onClick={() => setEditing(false)}>Vazgeç</Button>
        </form>
      ) : (
        <>
          <span className="flex flex-1 items-center gap-2">
            {table.name}
            {table.openSession && <Badge tone="amber">Dolu</Badge>}
            {!table.isActive && <Badge tone="gray">Kullanım dışı</Badge>}
          </span>
          <Button variant="link" onClick={() => setEditing(true)}>Adını değiştir</Button>
          <Button
            variant={table.isActive ? 'danger' : 'secondary'}
            disabled={busy}
            onClick={() => onSave({ id: table.id, name: table.name, isActive: !table.isActive })}
          >
            {table.isActive ? 'Kullanım dışı bırak' : 'Kullanıma aç'}
          </Button>
        </>
      )}
    </li>
  )
}

import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { User, UserRole } from '../../api/types'
import { roleLabels } from '../../auth/roles'
import { useAuth } from '../../auth/useAuth'
import { Badge, Button, Card, Input, Message } from './ui'

const ALL_ROLES: UserRole[] = ['Owner', 'Manager', 'Waiter', 'Cashier', 'Kitchen']
const isPin = (value: string) => /^\d{4}$/.test(value)

/**
 * Personel yönetimi: ekleme (ad, rol, PIN), rol/ad değiştirme, PIN değiştirme, pasife alma.
 * Yönetici, sahip ve yönetici hesaplarına dokunamaz ve bu rolleri veremez (kendi PIN'ini değiştirebilir).
 */
export function StaffTab() {
  const { user: me } = useAuth()
  const queryClient = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: () => api<User[]>('GET', '/users') })
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const isOwner = me?.role === 'Owner'
  const assignableRoles = isOwner ? ALL_ROLES : ALL_ROLES.filter((r) => r !== 'Owner' && r !== 'Manager')

  const done = (message: string) => {
    setError(null)
    setSuccess(message)
    return queryClient.invalidateQueries({ queryKey: ['users'] })
  }
  const failed = (err: Error) => {
    setSuccess(null)
    setError(err.message)
  }

  const create = useMutation({
    mutationFn: (u: { displayName: string; role: UserRole; pin: string }) => api<User>('POST', '/users', u),
    onSuccess: (u) => done(`${u.displayName} eklendi.`),
    onError: failed,
  })
  const update = useMutation({
    mutationFn: (u: User) => api<User>('PUT', `/users/${u.id}`, { displayName: u.displayName, role: u.role, branchId: u.branchId, isActive: u.isActive }),
    onSuccess: (u) => done(`${u.displayName} güncellendi.`),
    onError: failed,
  })
  const changePin = useMutation({
    mutationFn: ({ user, pin }: { user: User; pin: string }) => api('PUT', `/users/${user.id}/pin`, { pin }),
    onSuccess: (_, { user }) => done(`${user.displayName} için PIN değiştirildi.`),
    onError: failed,
  })

  return (
    <div className="space-y-4">
      <Message error={error} success={success} />
      <NewStaffForm roles={assignableRoles} busy={create.isPending} onCreate={(u) => create.mutate(u)} />
      <Card title="Personel">
        <ul className="divide-y divide-stone-100">
          {users.data?.map((u) => (
            <StaffRow
              key={u.id}
              user={u}
              isMe={u.id === me?.id}
              locked={!isOwner && (u.role === 'Owner' || u.role === 'Manager')}
              roles={assignableRoles}
              onUpdate={(x) => update.mutate(x)}
              onChangePin={(pin) => changePin.mutate({ user: u, pin })}
            />
          ))}
        </ul>
      </Card>
    </div>
  )
}

function NewStaffForm({ roles, busy, onCreate }: { roles: UserRole[]; busy: boolean; onCreate: (u: { displayName: string; role: UserRole; pin: string }) => void }) {
  const [name, setName] = useState('')
  const [role, setRole] = useState<UserRole>('Waiter')
  const [pin, setPin] = useState('')

  return (
    <Card title="Yeni personel">
      <form
        className="flex flex-wrap gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          onCreate({ displayName: name.trim(), role, pin })
          setName('')
          setPin('')
        }}
      >
        <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ad Soyad" maxLength={100} className="min-w-40 flex-1" />
        <select value={role} onChange={(e) => setRole(e.target.value as UserRole)} className="rounded-lg border border-stone-300 bg-white px-2 py-1.5 text-sm" aria-label="Rol">
          {roles.map((r) => <option key={r} value={r}>{roleLabels[r]}</option>)}
        </select>
        <PinInput value={pin} onChange={setPin} />
        <Button type="submit" variant="primary" disabled={busy || !name.trim() || !isPin(pin)}>Ekle</Button>
      </form>
    </Card>
  )
}

function StaffRow({
  user,
  isMe,
  locked,
  roles,
  onUpdate,
  onChangePin,
}: {
  user: User
  isMe: boolean
  locked: boolean
  roles: UserRole[]
  onUpdate: (u: User) => void
  onChangePin: (pin: string) => void
}) {
  const [mode, setMode] = useState<'view' | 'edit' | 'pin'>('view')
  const [name, setName] = useState(user.displayName)
  const [role, setRole] = useState(user.role)
  const [pin, setPin] = useState('')

  if (mode === 'edit') {
    return (
      <li className="py-2">
        <form
          className="flex flex-wrap gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            onUpdate({ ...user, displayName: name.trim(), role })
            setMode('view')
          }}
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} className="min-w-40 flex-1" autoFocus />
          <select
            value={role}
            disabled={isMe}
            onChange={(e) => setRole(e.target.value as UserRole)}
            className="rounded-lg border border-stone-300 bg-white px-2 py-1.5 text-sm disabled:opacity-50"
            aria-label="Rol"
          >
            {(roles.includes(user.role) ? roles : [user.role, ...roles]).map((r) => <option key={r} value={r}>{roleLabels[r]}</option>)}
          </select>
          <Button type="submit" variant="primary" disabled={!name.trim()}>Kaydet</Button>
          <Button onClick={() => setMode('view')}>Vazgeç</Button>
        </form>
      </li>
    )
  }

  if (mode === 'pin') {
    return (
      <li className="py-2">
        <form
          className="flex flex-wrap items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            onChangePin(pin)
            setPin('')
            setMode('view')
          }}
        >
          <span className="flex-1">{user.displayName} için yeni PIN</span>
          <PinInput value={pin} onChange={setPin} autoFocus />
          <Button type="submit" variant="primary" disabled={!isPin(pin)}>PIN'i değiştir</Button>
          <Button onClick={() => setMode('view')}>Vazgeç</Button>
        </form>
      </li>
    )
  }

  return (
    <li className={`flex flex-wrap items-center gap-2 py-2 ${user.isActive ? '' : 'text-stone-400'}`}>
      <span className="flex flex-1 items-center gap-2">
        {user.displayName}
        {isMe && <Badge tone="amber">Siz</Badge>}
        {!user.isActive && <Badge tone="gray">Pasif</Badge>}
      </span>
      <span className="w-28 text-sm text-stone-600">{roleLabels[user.role]}</span>
      {locked ? (
        <span className="text-xs text-stone-400">Yalnızca işletme sahibi düzenleyebilir</span>
      ) : (
        <>
          <Button variant="link" onClick={() => setMode('edit')}>Düzenle</Button>
          <Button variant="link" onClick={() => setMode('pin')}>PIN</Button>
          {!isMe && (
            <Button variant={user.isActive ? 'danger' : 'secondary'} onClick={() => onUpdate({ ...user, isActive: !user.isActive })}>
              {user.isActive ? 'Pasife al' : 'Aktif et'}
            </Button>
          )}
        </>
      )}
      {locked && isMe && <Button variant="link" onClick={() => setMode('pin')}>PIN'imi değiştir</Button>}
    </li>
  )
}

function PinInput({ value, onChange, autoFocus = false }: { value: string; onChange: (v: string) => void; autoFocus?: boolean }) {
  return (
    <Input
      value={value}
      onChange={(e) => onChange(e.target.value.replace(/\D/g, '').slice(0, 4))}
      placeholder="PIN (4 rakam)"
      inputMode="numeric"
      autoComplete="off"
      autoFocus={autoFocus}
      className="w-32 text-center font-mono tracking-widest"
      aria-label="PIN"
    />
  )
}

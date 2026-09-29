import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Branch, Device, SystemInfo } from '../../api/types'
import { Badge, Button, Card, Message } from './ui'

/**
 * Cihazlar: yeni bilgisayar/tablet bağlamak için eşleştirme kodu üretme ve kaybolan cihazın bağlantısını kesme.
 */
export function DevicesTab() {
  const queryClient = useQueryClient()
  const devices = useQuery({ queryKey: ['devices'], queryFn: () => api<Device[]>('GET', '/devices') })
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => api<Branch[]>('GET', '/branches') })
  const system = useQuery({ queryKey: ['system-info'], queryFn: () => api<SystemInfo>('GET', '/system/info') })
  const [code, setCode] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const newCode = useMutation({
    mutationFn: (branchId: string) => api<{ pairingCode: string }>('POST', `/branches/${branchId}/pairing-code`),
    onSuccess: (r) => {
      setError(null)
      setCode(r.pairingCode)
    },
    onError: (err) => setError(err.message),
  })
  const revoke = useMutation({
    mutationFn: (id: string) => api('POST', `/devices/${id}/revoke`),
    onSuccess: () => {
      setError(null)
      return queryClient.invalidateQueries({ queryKey: ['devices'] })
    },
    onError: (err) => setError(err.message),
  })

  // Şimdilik tek şube; birden çok şube olunca burada şube seçimi eklenecek.
  const branch = branches.data?.[0]

  return (
    <div className="space-y-4">
      <Message error={error} />

      <Card title="Diğer cihazlar hangi adresi açacak?">
        {system.data && system.data.addresses.length > 0 ? (
          <>
            <p className="text-sm text-stone-600">Restorandaki tablet veya bilgisayarın tarayıcısında şu adresi açın (aynı Wi-Fi'da olmalı):</p>
            <ul className="mt-2 space-y-1">
              {system.data.addresses.map((a) => (
                <li key={a} className="font-mono text-xl font-bold text-stone-900 select-all">{a}</li>
              ))}
            </ul>
          </>
        ) : (
          <p className="text-sm text-stone-600">Bu bilgisayar bir ağa bağlı görünmüyor. Wi-Fi veya kablo bağlantısını kontrol edin.</p>
        )}
      </Card>

      <Card
        title="Yeni cihaz bağla"
        actions={
          <Button variant="primary" disabled={!branch || newCode.isPending} onClick={() => branch && newCode.mutate(branch.id)}>
            Eşleştirme kodu oluştur
          </Button>
        }
      >
        {code ? (
          <div className="rounded-xl bg-amber-50 p-4 text-center ring-1 ring-amber-200">
            <p className="text-sm text-amber-900">Yeni cihazın kurulum ekranına bu kodu girin:</p>
            <p className="mt-2 font-mono text-3xl font-bold tracking-widest text-stone-900 select-all">{code}</p>
            <p className="mt-2 text-xs text-amber-900">
              Kod, yenisi oluşturulana kadar geçerlidir. Eski kod artık çalışmaz; daha önce bağlanan cihazlar etkilenmez.
            </p>
          </div>
        ) : (
          <p className="text-sm text-stone-600">
            Restorana yeni bir bilgisayar veya tablet eklerken buradan kod oluşturun ve o cihazın kurulum ekranına girin.
          </p>
        )}
      </Card>

      <Card title="Bağlı cihazlar">
        <ul className="divide-y divide-stone-100">
          {devices.data?.map((d) => (
            <li key={d.id} className={`flex flex-wrap items-center gap-2 py-2 ${d.isActive ? '' : 'text-stone-400'}`}>
              <span className="flex flex-1 items-center gap-2">
                {d.name}
                {d.isCurrent && <Badge tone="amber">Bu cihaz</Badge>}
                {!d.isActive && <Badge tone="gray">Bağlantı kesildi</Badge>}
              </span>
              <span className="text-xs text-stone-500">
                {d.lastSeenAt ? `Son giriş: ${new Date(d.lastSeenAt).toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' })}` : 'Hiç giriş yapılmadı'}
              </span>
              {d.isActive && !d.isCurrent && (
                <Button
                  variant="danger"
                  disabled={revoke.isPending}
                  onClick={() => {
                    if (confirm(`"${d.name}" cihazının bağlantısı kesilsin mi? Bu cihazda artık giriş yapılamaz.`)) revoke.mutate(d.id)
                  }}
                >
                  Bağlantıyı kes
                </Button>
              )}
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}

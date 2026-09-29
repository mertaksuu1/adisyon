import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { BackupInfo, SystemInfo } from '../../api/types'
import { Badge, Button, Card, Message } from './ui'

/**
 * Yedekler: her gün otomatik alınır (04:00'te veya bilgisayar açılınca); burada son yedek görülür ve
 * elle yedek alınabilir.
 */
export function BackupsTab() {
  const queryClient = useQueryClient()
  const backups = useQuery({ queryKey: ['backups'], queryFn: () => api<BackupInfo[]>('GET', '/system/backups') })
  const system = useQuery({ queryKey: ['system-info'], queryFn: () => api<SystemInfo>('GET', '/system/info') })
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const backupNow = useMutation({
    mutationFn: () => api<BackupInfo>('POST', '/system/backups'),
    onSuccess: () => {
      setError(null)
      setSuccess('Yedek alındı.')
      queryClient.invalidateQueries({ queryKey: ['backups'] })
      queryClient.invalidateQueries({ queryKey: ['system-info'] })
    },
    onError: (err) => {
      setSuccess(null)
      setError(err.message)
    },
  })

  const latest = backups.data?.[0]
  // Yedeğin yaşı, listenin sunucudan alındığı ana göre (çizim sırasında Date.now() kullanılmaz).
  const hoursSince = latest ? (backups.dataUpdatedAt - new Date(latest.createdAt).getTime()) / 3_600_000 : null

  return (
    <div className="space-y-4">
      <Message error={error} success={success} />

      <Card
        title="Son yedek"
        actions={
          <Button variant="primary" disabled={backupNow.isPending} onClick={() => backupNow.mutate()}>
            {backupNow.isPending ? 'Yedekleniyor…' : 'Şimdi yedek al'}
          </Button>
        }
      >
        {latest ? (
          <p className="flex items-center gap-2 text-lg">
            {formatDateTime(latest.createdAt)}
            {hoursSince !== null && hoursSince > 48 ? <Badge tone="amber">Eski</Badge> : <Badge tone="green">Güncel</Badge>}
          </p>
        ) : (
          <p className="text-stone-600">Henüz yedek yok. "Şimdi yedek al" ile ilk yedeği alın.</p>
        )}
        <p className="mt-2 text-sm text-stone-600">
          Yedekler her gün otomatik alınır (04:00'te; bilgisayar kapalıysa açılınca). Son 30 yedek saklanır.
        </p>
        {system.data && (
          <p className="mt-1 text-xs break-all text-stone-500">Klasör: {system.data.backupDirectory}</p>
        )}
        <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
          Bilgisayar tamamen bozulursa bu klasör de gider. Ara sıra klasörü bir USB belleğe kopyalayın.
        </p>
      </Card>

      <Card title={`Yedekler · ${backups.data?.length ?? 0}`}>
        <ul className="divide-y divide-stone-100 text-sm">
          {backups.data?.map((b) => (
            <li key={b.fileName} className="flex justify-between py-1.5">
              <span>{formatDateTime(b.createdAt)}</span>
              <span className="text-stone-500 tabular-nums">{(b.sizeBytes / 1024).toFixed(0)} KB</span>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

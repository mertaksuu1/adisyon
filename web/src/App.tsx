import { useEffect, useState } from 'react'

type Health = { api: string; database: string }

type Status = 'loading' | 'ok' | 'error'

export default function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    fetch('/api/health')
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json() as Promise<Health>
      })
      .then(setHealth)
      .catch(() => setFailed(true))
  }, [])

  const apiStatus: Status = failed ? 'error' : health ? 'ok' : 'loading'
  const dbStatus: Status = failed
    ? 'error'
    : !health
      ? 'loading'
      : health.database === 'ok'
        ? 'ok'
        : 'error'

  return (
    <main className="min-h-screen bg-stone-50 px-4 py-16 text-stone-900">
      <div className="mx-auto max-w-md">
        <h1 className="text-3xl font-bold">Adisyon</h1>
        <p className="mt-2 text-stone-600">Geliştirme ortamı durumu</p>

        <ul className="mt-8 divide-y divide-stone-200 rounded-lg border border-stone-200 bg-white">
          <StatusRow label="Web arayüzü" status="ok" />
          <StatusRow label="API" status={apiStatus} />
          <StatusRow label="Veritabanı" status={dbStatus} />
        </ul>

        {apiStatus === 'error' && (
          <p className="mt-4 text-sm text-red-700">
            API'ye ulaşılamadı. Başka bir terminalde <code>dotnet run --project src/Adisyon.Api</code>{' '}
            çalışıyor mu?
          </p>
        )}
        {apiStatus === 'ok' && dbStatus === 'error' && (
          <p className="mt-4 text-sm text-red-700">
            Veritabanına ulaşılamadı. Docker Desktop açık mı ve <code>docker compose up -d</code>{' '}
            çalıştırıldı mı?
          </p>
        )}
      </div>
    </main>
  )
}

const statusText: Record<Status, string> = {
  loading: 'Kontrol ediliyor…',
  ok: 'Çalışıyor',
  error: 'Hata',
}

const statusColor: Record<Status, string> = {
  loading: 'text-stone-500',
  ok: 'text-green-700',
  error: 'text-red-700',
}

function StatusRow({ label, status }: { label: string; status: Status }) {
  return (
    <li className="flex items-center justify-between px-4 py-3">
      <span>{label}</span>
      <span className={`font-medium ${statusColor[status]}`}>{statusText[status]}</span>
    </li>
  )
}

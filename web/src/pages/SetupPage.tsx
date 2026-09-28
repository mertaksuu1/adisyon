import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useAuth } from '../auth/useAuth'

/** Sunucudaki DevDataSeeder.DemoPairingCode ile aynı olmalı. Yalnızca geliştirmede kullanılır. */
const DEV_DEMO_PAIRING_CODE = 'DEMO-DEMO-DEMO'

/**
 * İlk kurulum: bilgisayarı şubenin eşleştirme koduyla restorana bağlar. Her cihazda bir kez yapılır.
 */
export default function SetupPage() {
  const { pair } = useAuth()
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [deviceName, setDeviceName] = useState('Kasa bilgisayarı')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    await connect(code)
  }

  async function connect(pairingCode: string) {
    setBusy(true)
    setError(null)
    try {
      await pair(pairingCode, deviceName)
      navigate('/giris', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Eşleştirme başarısız.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-stone-100 px-4">
      <form onSubmit={submit} className="w-full max-w-md rounded-3xl bg-white p-8 shadow-sm ring-1 ring-stone-200">
        <h1 className="text-2xl font-bold text-stone-900">Cihaz kurulumu</h1>
        <p className="mt-2 text-stone-600">
          Bu bilgisayarı restorana bağlamak için şubenin eşleştirme kodunu girin. Bu işlem bir kez yapılır.
        </p>

        <label className="mt-6 block text-sm font-medium text-stone-700" htmlFor="code">
          Eşleştirme kodu
        </label>
        <input
          id="code"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          placeholder="XXXX-XXXX-XXXX"
          autoComplete="off"
          autoFocus
          required
          className="mt-1 w-full rounded-xl border border-stone-300 px-4 py-3 font-mono text-lg tracking-widest uppercase focus:border-amber-600 focus:ring-2 focus:ring-amber-200 focus:outline-none"
        />

        <label className="mt-4 block text-sm font-medium text-stone-700" htmlFor="name">
          Bu cihazın adı
        </label>
        <input
          id="name"
          value={deviceName}
          onChange={(e) => setDeviceName(e.target.value)}
          required
          maxLength={100}
          className="mt-1 w-full rounded-xl border border-stone-300 px-4 py-3 focus:border-amber-600 focus:ring-2 focus:ring-amber-200 focus:outline-none"
        />
        <p className="mt-1 text-xs text-stone-500">Örn. "Kasa bilgisayarı", "Mutfak ekranı", "Garson tableti 1"</p>

        {error && <p className="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

        <button
          type="submit"
          disabled={busy}
          className="mt-6 w-full rounded-xl bg-amber-600 py-3 text-lg font-semibold text-white transition hover:bg-amber-700 disabled:opacity-50"
        >
          {busy ? 'Bağlanıyor…' : 'Cihazı bağla'}
        </button>

        {/* Yalnızca "npm run dev" ile çalışırken görünür; gerçek kurulum paketinde bu blok ve kod hiç yer almaz. */}
        {import.meta.env.DEV && (
          <div className="mt-6 rounded-xl border border-dashed border-stone-300 p-3 text-center">
            <p className="text-xs text-stone-500">Geliştirme ortamı</p>
            <button
              type="button"
              disabled={busy}
              onClick={() => connect(DEV_DEMO_PAIRING_CODE)}
              className="mt-1 text-sm font-semibold text-amber-700 hover:underline disabled:opacity-50"
            >
              Demo restoranı bağla
            </button>
          </div>
        )}
      </form>
    </main>
  )
}

import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { homePathFor } from '../auth/roles'
import { PinPad } from '../components/PinPad'

/** Sunucudaki DevDataSeeder.DemoStaff ile aynı olmalı. Yalnızca geliştirmede kullanılır. */
const DEV_DEMO_STAFF = [
  ['Sahip', '1111'],
  ['Yönetici', '2222'],
  ['Garson', '3333'],
  ['Mutfak', '4444'],
  ['Kasa', '5555'],
] as const

/** Personel girişi: yalnızca 4 haneli PIN. PIN'e göre rolün ekranı açılır. */
export default function PinPage() {
  const { device, loginWithPin, unpair } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  async function submit(pin: string) {
    setError(null)
    try {
      const user = await loginWithPin(pin)
      navigate(homePathFor(user.role), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Giriş başarısız.')
    }
  }

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-10 bg-stone-100 px-4 py-10">
      <header className="text-center">
        <p className="text-sm font-medium tracking-wide text-amber-700 uppercase">{device?.branchName}</p>
        <h1 className="mt-1 text-3xl font-bold text-stone-900">{device?.tenantName}</h1>
        <p className="mt-3 text-stone-600">PIN kodunuzu girin</p>
      </header>

      <PinPad onComplete={submit} />

      <p className="min-h-6 text-center font-medium text-red-700" role="alert">
        {error}
      </p>

      {/* Yalnızca "npm run dev" ile çalışırken görünür; gerçek kurulum paketinde yer almaz. */}
      {import.meta.env.DEV && device?.tenantName === 'Demo Restoran' && (
        <div className="rounded-xl border border-dashed border-stone-300 p-3 text-center">
          <p className="text-xs text-stone-500">Geliştirme ortamı · demo personelle hızlı giriş</p>
          <div className="mt-2 flex flex-wrap justify-center gap-2">
            {DEV_DEMO_STAFF.map(([label, pin]) => (
              <button
                key={pin}
                type="button"
                onClick={() => submit(pin)}
                className="rounded-lg bg-white px-3 py-1.5 text-sm font-medium text-stone-700 ring-1 ring-stone-200 hover:ring-amber-400"
              >
                {label}
              </button>
            ))}
          </div>
        </div>
      )}

      <button
        type="button"
        onClick={() => {
          if (confirm('Bu cihazın restoranla bağlantısı kaldırılsın mı? Yeniden eşleştirme kodu gerekecek.')) unpair()
        }}
        className="text-xs text-stone-400 underline-offset-2 hover:underline"
      >
        Cihaz bağlantısını kaldır
      </button>
    </main>
  )
}

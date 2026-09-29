import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../api/client'
import type { SetupResponse } from '../api/types'
import { useAuth } from '../auth/useAuth'

/**
 * İlk kurulum sihirbazı: program boş bir bilgisayarda ilk açıldığında restoranı ve işletme sahibini oluşturur.
 * Bir kez yapılır; sonraki cihazlar eşleştirme koduyla bağlanır.
 */
export function FirstRunForm() {
  const { completeSetup } = useAuth()
  const navigate = useNavigate()
  const [restaurantName, setRestaurantName] = useState('')
  const [branchName, setBranchName] = useState('Merkez')
  const [ownerName, setOwnerName] = useState('')
  const [pin, setPin] = useState('')
  const [pinAgain, setPinAgain] = useState('')
  const [deviceName, setDeviceName] = useState('Kasa bilgisayarı')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const pinValid = /^\d{4}$/.test(pin)
  const pinsMatch = pin === pinAgain
  const canSubmit = restaurantName.trim() && branchName.trim() && ownerName.trim() && deviceName.trim() && pinValid && pinsMatch

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const result = await api<SetupResponse>('POST', '/setup', {
        restaurantName: restaurantName.trim(),
        branchName: branchName.trim(),
        ownerName: ownerName.trim(),
        ownerPin: pin,
        deviceName: deviceName.trim(),
      })
      completeSetup(result)
      navigate('/ana-menu', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kurulum başarısız.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="w-full max-w-md rounded-3xl bg-white p-8 shadow-sm ring-1 ring-stone-200">
      <h1 className="text-2xl font-bold text-stone-900">Hoş geldiniz</h1>
      <p className="mt-2 text-stone-600">Restoranınızı kuralım. Bu bilgiler sonradan yönetim ekranından değiştirilebilir.</p>

      <Field label="Restoran adı" htmlFor="restaurant">
        <input id="restaurant" value={restaurantName} onChange={(e) => setRestaurantName(e.target.value)} maxLength={200} autoFocus required placeholder="ör. Kebapçı Mehmet" className={inputClass} />
      </Field>
      <Field label="Şube adı" htmlFor="branch">
        <input id="branch" value={branchName} onChange={(e) => setBranchName(e.target.value)} maxLength={200} required className={inputClass} />
      </Field>
      <Field label="İşletme sahibinin adı" htmlFor="owner">
        <input id="owner" value={ownerName} onChange={(e) => setOwnerName(e.target.value)} maxLength={100} required placeholder="ör. Mehmet Yılmaz" className={inputClass} />
      </Field>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <Field label="Sahip PIN'i (4 rakam)" htmlFor="pin" noMargin>
          <input id="pin" value={pin} onChange={(e) => setPin(onlyDigits(e.target.value))} inputMode="numeric" autoComplete="off" required className={`${inputClass} text-center font-mono tracking-widest`} />
        </Field>
        <Field label="PIN tekrar" htmlFor="pin2" noMargin>
          <input id="pin2" value={pinAgain} onChange={(e) => setPinAgain(onlyDigits(e.target.value))} inputMode="numeric" autoComplete="off" required className={`${inputClass} text-center font-mono tracking-widest`} />
        </Field>
      </div>
      {pinAgain.length === 4 && !pinsMatch && <p className="mt-1 text-sm text-red-700">PIN'ler aynı değil.</p>}
      <p className="mt-1 text-xs text-stone-500">Bu PIN'i unutmayın: yönetim ekranına yalnızca işletme sahibi girebilir.</p>

      <Field label="Bu bilgisayarın adı" htmlFor="device">
        <input id="device" value={deviceName} onChange={(e) => setDeviceName(e.target.value)} maxLength={100} required className={inputClass} />
      </Field>

      {error && <p className="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</p>}

      <button
        type="submit"
        disabled={!canSubmit || busy}
        className="mt-6 w-full rounded-xl bg-amber-600 py-3 text-lg font-semibold text-white transition hover:bg-amber-700 disabled:opacity-50"
      >
        {busy ? 'Kuruluyor…' : 'Restoranı kur'}
      </button>
    </form>
  )
}

const inputClass =
  'mt-1 w-full rounded-xl border border-stone-300 px-4 py-3 focus:border-amber-600 focus:ring-2 focus:ring-amber-200 focus:outline-none'

const onlyDigits = (value: string) => value.replace(/\D/g, '').slice(0, 4)

function Field({ label, htmlFor, noMargin = false, children }: { label: string; htmlFor: string; noMargin?: boolean; children: React.ReactNode }) {
  return (
    <div className={noMargin ? '' : 'mt-4'}>
      <label htmlFor={htmlFor} className="block text-sm font-medium text-stone-700">{label}</label>
      {children}
    </div>
  )
}

import { useCallback, useEffect, useState } from 'react'

const PIN_LENGTH = 4

type Props = {
  /** 4. rakam girilince çağrılır. Hata olursa PinPad kutuları temizler. */
  onComplete: (pin: string) => Promise<void>
  disabled?: boolean
}

/**
 * Dokunmatik ekran için büyük tuşlu PIN klavyesi. Fiziksel klavyeden rakam, Backspace ve Escape de çalışır.
 */
export function PinPad({ onComplete, disabled = false }: Props) {
  const [pin, setPin] = useState('')
  const [busy, setBusy] = useState(false)
  const locked = disabled || busy

  const press = useCallback(
    (digit: string) => {
      if (locked || pin.length >= PIN_LENGTH) return
      const next = pin + digit
      setPin(next)
      // Sunucuya gönderme bir yan etkidir; setPin(fn) içine konmaz, çünkü React geliştirme
      // modunda o fonksiyonları iki kez çalıştırır ve PIN iki kez gönderilirdi.
      if (next.length === PIN_LENGTH) {
        setBusy(true)
        onComplete(next).finally(() => {
          setBusy(false)
          setPin('')
        })
      }
    },
    [locked, pin, onComplete],
  )

  const backspace = useCallback(() => !locked && setPin((p) => p.slice(0, -1)), [locked])
  const clear = useCallback(() => !locked && setPin(''), [locked])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) press(e.key)
      else if (e.key === 'Backspace') backspace()
      else if (e.key === 'Escape') clear()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [press, backspace, clear])

  return (
    <div className="flex flex-col items-center gap-8">
      <div className="flex gap-4" aria-label={`${pin.length} rakam girildi`}>
        {Array.from({ length: PIN_LENGTH }, (_, i) => (
          <span
            key={i}
            className={`size-5 rounded-full border-2 transition-colors ${
              i < pin.length ? 'border-amber-600 bg-amber-600' : 'border-stone-300'
            }`}
          />
        ))}
      </div>

      <div className="grid grid-cols-3 gap-3">
        {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((d) => (
          <Key key={d} onClick={() => press(d)} disabled={locked}>
            {d}
          </Key>
        ))}
        <Key onClick={clear} disabled={locked} muted aria-label="Temizle">
          Sil
        </Key>
        <Key onClick={() => press('0')} disabled={locked}>
          0
        </Key>
        <Key onClick={backspace} disabled={locked} muted aria-label="Son rakamı sil">
          ⌫
        </Key>
      </div>
    </div>
  )
}

function Key({
  children,
  muted = false,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { muted?: boolean }) {
  return (
    <button
      type="button"
      className={`size-20 rounded-2xl text-2xl font-semibold shadow-sm transition active:scale-95 disabled:opacity-40 sm:size-24 ${
        muted ? 'bg-stone-100 text-base text-stone-600' : 'bg-white text-stone-900 ring-1 ring-stone-200'
      }`}
      {...props}
    >
      {children}
    </button>
  )
}

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })

/** 1020 → "₺1.020,00" */
export function formatMoney(value: number) {
  return money.format(value)
}

/** Masanın ne kadar süredir açık olduğu: "12 dk", "1 sa 5 dk". */
export function formatDuration(since: string, now = new Date()) {
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(since).getTime()) / 60_000))
  if (minutes < 60) return `${minutes} dk`
  return `${Math.floor(minutes / 60)} sa ${minutes % 60} dk`
}

/** "14:05" */
export function formatTime(value: string) {
  return new Date(value).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

/** "1.250,50" veya "1250.5" → 1250.5; geçersizse null. */
export function parseAmount(text: string): number | null {
  const trimmed = text.trim()
  if (!trimmed) return null
  const normalized = trimmed.includes(',') ? trimmed.replace(/\./g, '').replace(',', '.') : trimmed
  const value = Number(normalized)
  return Number.isFinite(value) ? Math.round(value * 100) / 100 : null
}

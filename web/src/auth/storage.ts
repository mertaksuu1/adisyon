// Cihaz ve oturum bilgisini tarayıcıda (localStorage) saklar.
// - Cihaz bilgisi kalıcıdır: bir kez eşleştirilir, aylarca kalır.
// - Oturum (PIN girişi) çıkışta veya süresi dolunca silinir.
// localStorage bazı durumlarda (gizli pencere vb.) hata fırlatabilir; bu yüzden her erişim try/catch içinde.

import type { LoginResponse, PairDeviceResponse } from '../api/types'

const DEVICE_KEY = 'adisyon.device'
const SESSION_KEY = 'adisyon.session'

export type StoredDevice = PairDeviceResponse
export type StoredSession = LoginResponse

function read<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as T) : null
  } catch {
    return null
  }
}

function write(key: string, value: unknown) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // Saklanamazsa uygulama yine çalışır; sadece sayfa yenilenince tekrar giriş gerekir.
  }
}

export const storage = {
  getDevice: () => read<StoredDevice>(DEVICE_KEY),
  setDevice: (device: StoredDevice | null) => write(DEVICE_KEY, device),

  getSession: () => {
    const session = read<StoredSession>(SESSION_KEY)
    // Süresi dolmuş oturumu hiç yokmuş gibi say.
    if (session && new Date(session.expiresAt) <= new Date()) {
      write(SESSION_KEY, null)
      return null
    }
    return session
  },
  setSession: (session: StoredSession | null) => write(SESSION_KEY, session),
}

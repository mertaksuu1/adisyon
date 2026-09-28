import { createContext, useContext } from 'react'
import type { User } from '../api/types'
import type { StoredDevice } from './storage'

type AuthState = {
  /** Eşleştirilmiş cihaz; null ise kurulum ekranı gösterilmeli. */
  device: StoredDevice | null
  /** PIN ile giriş yapmış kullanıcı; null ise PIN ekranı gösterilmeli. */
  user: User | null
  pair: (pairingCode: string, deviceName: string) => Promise<void>
  loginWithPin: (pin: string) => Promise<User>
  logout: () => void
  /** Cihaz eşleştirmesini kaldırır (ör. bilgisayar başka şubeye taşınınca). */
  unpair: () => void
}

export const AuthContext = createContext<AuthState | null>(null)

/** Herhangi bir bileşenden cihaz ve kullanıcı bilgisine ulaşmak için: const { user } = useAuth() */
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth, AuthProvider içinde kullanılmalı.')
  return context
}

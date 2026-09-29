import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, setUnauthorizedHandler } from '../api/client'
import type { LoginResponse, PairDeviceResponse, SetupResponse } from '../api/types'
import { storage } from './storage'
import { AuthContext } from './useAuth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [device, setDevice] = useState(() => storage.getDevice())
  const [user, setUser] = useState(() => storage.getSession()?.user ?? null)

  const logout = useCallback(() => {
    storage.setSession(null)
    setUser(null)
    // Bir sonraki personel önceki kişinin ekrandaki verisini görmesin.
    queryClient.clear()
  }, [queryClient])

  // Token süresi dolar veya geçersiz olursa API 401 döner; o zaman PIN ekranına dön.
  useEffect(() => {
    setUnauthorizedHandler(logout)
    return () => setUnauthorizedHandler(null)
  }, [logout])

  const pair = useCallback(async (pairingCode: string, deviceName: string) => {
    const result = await api<PairDeviceResponse>('POST', '/auth/pair', { pairingCode, deviceName })
    storage.setDevice(result)
    setDevice(result)
  }, [])

  const loginWithPin = useCallback(async (pin: string) => {
    const result = await api<LoginResponse>('POST', '/auth/pin-login', { pin })
    storage.setSession(result)
    setUser(result.user)
    return result.user
  }, [])

  const completeSetup = useCallback((result: SetupResponse) => {
    storage.setDevice(result.device)
    storage.setSession(result.login)
    setDevice(result.device)
    setUser(result.login.user)
  }, [])

  const unpair = useCallback(() => {
    logout()
    storage.setDevice(null)
    setDevice(null)
  }, [logout])

  const value = useMemo(
    () => ({ device, user, pair, loginWithPin, completeSetup, logout, unpair }),
    [device, user, pair, loginWithPin, completeSetup, logout, unpair],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

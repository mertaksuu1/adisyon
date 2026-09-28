import { createContext, useContext, useEffect, useRef } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import type { KitchenOrder } from '../api/types'

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting'

/** Sunucunun gönderdiği olaylar (sunucudaki RealtimeEvents ile aynı adlar). */
export type RealtimeEvents = {
  OrderCreated: (order: KitchenOrder) => void
  OrderUpdated: (order: KitchenOrder) => void
  TablesChanged: () => void
}

type RealtimeState = {
  connection: HubConnection | null
  status: ConnectionStatus
}

export const RealtimeContext = createContext<RealtimeState>({ connection: null, status: 'connecting' })

/** Bağlantı durumu; "yeniden bağlanıyor" uyarısını göstermek için. */
export function useConnectionStatus() {
  return useContext(RealtimeContext).status
}

/**
 * Bir sunucu olayını dinler. Örnek (mutfakta zil):
 *   useRealtimeEvent('OrderCreated', () => playBell())
 */
export function useRealtimeEvent<E extends keyof RealtimeEvents>(event: E, handler: RealtimeEvents[E]) {
  const { connection } = useContext(RealtimeContext)
  // Her render'da yeni fonksiyon gelse bile yeniden abone olmamak için son handler'ı ref'te tutuyoruz.
  const handlerRef = useRef(handler)
  useEffect(() => {
    handlerRef.current = handler
  })

  useEffect(() => {
    if (!connection) return
    const listener = (...args: unknown[]) => (handlerRef.current as (...a: unknown[]) => void)(...args)
    connection.on(event, listener)
    return () => connection.off(event, listener)
  }, [connection, event])
}

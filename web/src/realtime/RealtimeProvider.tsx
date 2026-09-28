import { useEffect, useState, type ReactNode } from 'react'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { storage } from '../auth/storage'
import { useAuth } from '../auth/useAuth'
import { RealtimeContext, type ConnectionStatus } from './useRealtime'

/**
 * Giriş yapılmışken sunucuyla sürekli açık bir SignalR bağlantısı tutar.
 *
 * Temel fikir: sunucudan gelen olaylar yalnızca "şu değişti" sinyalidir; ekranlar veriyi her zaman
 * normal API'den çeker. Bağlantı kopup geri gelince TÜM veri yeniden çekilir. Böylece kopukluk
 * sırasında kaçan bir bildirim hiçbir zaman kayıp sipariş anlamına gelmez.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [connection, setConnection] = useState<HubConnection | null>(null)
  const [status, setStatus] = useState<ConnectionStatus>('connecting')

  useEffect(() => {
    if (!user) return

    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/branch', { accessTokenFactory: () => storage.getSession()?.token ?? '' })
      // Kopunca 0, 2, 5, 10 sn sonra, sonra her 15 sn'de bir yeniden dene; hiç vazgeçme.
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => [0, 2000, 5000, 10000][ctx.previousRetryCount] ?? 15000 })
      .configureLogging(LogLevel.Warning)
      .build()

    // Olay geldikçe ilgili verileri "bayat" işaretle; ekranda görünenler otomatik yeniden çekilir.
    conn.on('TablesChanged', () => {
      queryClient.invalidateQueries({ queryKey: ['tables'] })
      queryClient.invalidateQueries({ queryKey: ['session'] })
    })
    conn.on('OrderCreated', () => {
      queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] })
    })
    conn.on('OrderUpdated', () => {
      queryClient.invalidateQueries({ queryKey: ['kitchen-orders'] })
      queryClient.invalidateQueries({ queryKey: ['session'] })
    })

    conn.onreconnecting(() => setStatus('reconnecting'))
    conn.onreconnected(() => {
      setStatus('connected')
      queryClient.invalidateQueries() // kopukken kaçırılmış olabilecek her şeyi yeniden çek
    })

    let stopped = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    // İlk bağlantı başarısız olursa (ör. sunucu henüz açılmadı) otomatik yeniden bağlanma devreye girmez;
    // bu yüzden ilk bağlantıyı kendimiz tekrar deniyoruz.
    const start = async () => {
      try {
        await conn.start()
        if (stopped) return
        // Bağlantıyı ekranlara ancak kurulduğunda paylaşıyoruz; useRealtimeEvent aboneleri o an eklenir.
        setConnection(conn)
        setStatus('connected')
        queryClient.invalidateQueries() // bağlanana kadar kaçmış olabilecek her şeyi çek
      } catch {
        if (stopped) return
        setStatus('reconnecting')
        retryTimer = setTimeout(start, 5000)
      }
    }

    void start()

    return () => {
      stopped = true
      clearTimeout(retryTimer)
      setConnection(null)
      void conn.stop()
    }
  }, [user, queryClient])

  return <RealtimeContext.Provider value={{ connection, status }}>{children}</RealtimeContext.Provider>
}

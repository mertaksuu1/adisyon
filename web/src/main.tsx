import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router'
import { ApiError } from './api/client'
import App from './App.tsx'
import { AuthProvider } from './auth/AuthContext'
import { unlockSound } from './lib/bell'
import { ReadyNotifications } from './realtime/ReadyNotifications'
import { RealtimeProvider } from './realtime/RealtimeProvider'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Yetki ve "bulunamadı" hataları tekrar denemekle düzelmez; yalnızca bağlantı hatalarını tekrar dene.
      retry: (failureCount, error) =>
        failureCount < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
    },
  },
})

// Tarayıcılar ilk dokunuştan önce ses çalmaya izin vermez; ilk dokunuşta sesi sessizce aç.
window.addEventListener('pointerdown', unlockSound, { once: true })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <RealtimeProvider>
            <App />
            <ReadyNotifications />
          </RealtimeProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)

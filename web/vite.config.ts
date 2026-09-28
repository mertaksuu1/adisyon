import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // /api ile başlayan istekler geliştirme sırasında ASP.NET Core API'ye yönlendirilir.
    // Böylece tarayıcı tek adres görür, CORS ayarı gerekmez.
    proxy: {
      '/api': 'http://localhost:5260',
    },
  },
})

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Geliştirmede uygulama Vite'tan açılıyor; API, oturum ve Keycloak'ın dönüş adresleri
// compose'daki BFF'e gidiyor. Tarayıcı tek bir köken görüyor: cookie ve API aynı yerde.
// Host başlığı korunuyor, BFF Keycloak'a dönüş adresi olarak bu sunucuyu veriyor.
const bff = { target: 'http://localhost:8102' }

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/v1': bff,
      '/bff': bff,
      '/signin-oidc': bff,
      '/signout-callback-oidc': bff,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})

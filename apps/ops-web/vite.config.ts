import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // The Keycloak client registers exactly this origin, so never fall back to another port.
  // The shared design files live in ../../shared and ../../packages (fonts), outside this app.
  server: { port: 5173, strictPort: true, fs: { allow: ['../..'] } },
})

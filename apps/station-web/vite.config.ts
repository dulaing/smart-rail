import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'

// Writes /sw.js into the build, listing every built file (page, scripts, styles, bundled fonts) so
// the service worker can cache the whole shell for an offline reload. The cache name changes
// whenever the build does, which retires the old cache.
function stationServiceWorker(): Plugin {
  return {
    name: 'station-service-worker',
    apply: 'build',
    generateBundle(_options, bundle) {
      const files = ['/', ...Object.keys(bundle).map((name) => `/${name}`)]
      const hash = createHash('sha256').update(files.join('\n')).digest('hex').slice(0, 12)
      const template = readFileSync(new URL('./sw-template.js', import.meta.url), 'utf8')
      this.emitFile({
        type: 'asset',
        fileName: 'sw.js',
        source: template.replace('__CACHE_NAME__', `smartrail-station-${hash}`).replace('__SHELL_FILES__', JSON.stringify(files)),
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), stationServiceWorker()],
  // The Keycloak client registers exactly this origin, so never fall back to another port.
  // The shared design files live in ../../shared and ../../packages (fonts), outside this app.
  server: { port: 5173, strictPort: true, fs: { allow: ['../..'] } },
})

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../../../shared/design/generated/tokens.css'
import './design/design.css'
import './index.css'
import App from './App.tsx'

// The built app registers a service worker that caches the page, scripts, styles and fonts, so a
// board that reloads while offline still starts. It is not used by `vite dev`.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // The board works without it; only offline reload is lost.
    })
  })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

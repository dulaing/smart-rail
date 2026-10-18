import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../../../shared/design/generated/tokens.css'
import './design/design.css'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

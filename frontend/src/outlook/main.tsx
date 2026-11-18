import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource-variable/inter'
import './outlook.css'
import OutlookPane from './OutlookPane'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <OutlookPane />
  </StrictMode>,
)

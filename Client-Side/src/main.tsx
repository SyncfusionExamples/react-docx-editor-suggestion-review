import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { registerLicense } from '@syncfusion/ej2-base';

registerLicense('Your syncfusion license');
createRoot(document.getElementById('root')!).render(
    <App />
)

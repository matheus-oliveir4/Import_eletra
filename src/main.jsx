import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import FluxoE2E from './FluxoE2E.jsx';

const W = 1693, H = 1130;

/* Ajusta o fluxo à largura da tela, mantendo as proporções */
function Fit({ children }) {
  const [s, setS] = useState(1);
  useEffect(() => {
    const f = () => setS(Math.min(1, window.innerWidth / W));
    f();
    window.addEventListener('resize', f);
    return () => window.removeEventListener('resize', f);
  }, []);
  return (
    <div style={{ width: W * s, height: H * s, margin: '0 auto' }}>
      <div style={{ width: W, height: H, transform: `scale(${s})`, transformOrigin: 'top left' }}>{children}</div>
    </div>
  );
}

const lang = new URLSearchParams(window.location.search).get('lang') === 'zh' ? 'zh' : 'pt';
createRoot(document.getElementById('root')).render(<Fit><FluxoE2E lang={lang} /></Fit>);

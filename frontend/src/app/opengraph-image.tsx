import { ImageResponse } from 'next/og';

export const alt = 'Makables - Kde napady dostavaji tvar';
export const size = {
  width: 1200,
  height: 630,
};
export const contentType = 'image/png';

function StarLogo({ sizePx }: { sizePx: number }) {
  return (
    <svg width={sizePx} height={sizePx} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path
        d="M8.2 12c0-3.55 1.7-6 3.8-6s3.8 2.45 3.8 6-1.7 6-3.8 6-3.8-2.45-3.8-6z"
        stroke="#2dd4bf"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M12 8.2c3.55 0 6 1.7 6 3.8s-2.45 3.8-6 3.8-6-1.7-6-3.8 2.45-3.8 6-3.8z"
        stroke="#2dd4bf"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="12" cy="12" r="1.05" fill="#2dd4bf" />
    </svg>
  );
}

export default function OpenGraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: '100%',
          height: '100%',
          display: 'flex',
          position: 'relative',
          background: 'linear-gradient(120deg, #09090b 0%, #111827 45%, #052e2b 100%)',
          color: '#f4f4f5',
          fontFamily: 'ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif',
        }}
      >
        <div
          style={{
            position: 'absolute',
            top: -120,
            right: -80,
            width: 520,
            height: 520,
            borderRadius: 999,
            background: 'radial-gradient(circle, rgba(45,212,191,0.18) 0%, rgba(45,212,191,0) 72%)',
          }}
        />

        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            width: '100%',
            padding: '64px 72px',
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 20,
              fontSize: 36,
              fontWeight: 700,
              letterSpacing: '-0.01em',
            }}
          >
            <div
              style={{
                width: 62,
                height: 62,
                borderRadius: 16,
                background: 'rgba(9, 9, 11, 0.55)',
                border: '1px solid rgba(45,212,191,0.45)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <StarLogo sizePx={38} />
            </div>
            <span>Makables</span>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 860 }}>
            <div style={{ fontSize: 74, fontWeight: 800, lineHeight: 1.02, letterSpacing: '-0.03em' }}>
              Kde napady dostavaji tvar
            </div>
            <div style={{ fontSize: 34, color: '#d4d4d8', lineHeight: 1.25 }}>
              Marketplace pro makery a tiskare v CR
            </div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <div style={{ fontSize: 26, color: '#a1a1aa' }}>makables.cz</div>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                fontSize: 24,
                color: '#99f6e4',
                border: '1px solid rgba(45,212,191,0.35)',
                background: 'rgba(20, 184, 166, 0.12)',
                padding: '8px 14px',
                borderRadius: 999,
              }}
            >
              <span>Makables link preview</span>
            </div>
          </div>
        </div>
      </div>
    ),
    size,
  );
}

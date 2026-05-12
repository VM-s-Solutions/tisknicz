interface IconProps {
  name: keyof typeof icons;
  size?: number;
  className?: string;
}

export function Icon({ name, size = 24, className = '' }: IconProps) {
  const path = icons[name];
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
    >
      {path}
    </svg>
  );
}

const icons = {
  printer: (
    <>
      <path d="M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2" />
      <path d="M6 9V3a1 1 0 011-1h10a1 1 0 011 1v6" />
      <rect x="6" y="14" width="12" height="8" rx="1" />
    </>
  ),
  tshirt: (
    <>
      <path d="M15.5 2H8.5L4 6l2.5 2L8 6.5V22h8V6.5L17.5 8 20 6l-4.5-4z" />
    </>
  ),
  laser: (
    <>
      <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83" />
      <circle cx="12" cy="12" r="3" />
    </>
  ),
  package: (
    <>
      <path d="M16.5 9.4l-9-5.19M21 16V8a2 2 0 00-1-1.73l-7-4a2 2 0 00-2 0l-7 4A2 2 0 003 8v8a2 2 0 001 1.73l7 4a2 2 0 002 0l7-4A2 2 0 0021 16z" />
      <path d="M3.27 6.96L12 12.01l8.73-5.05M12 22.08V12" />
    </>
  ),
  star: (
    <>
      <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z" fill="currentColor" stroke="none" />
    </>
  ),
  starOutline: (
    <>
      <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z" />
    </>
  ),
  search: (
    <>
      <circle cx="11" cy="11" r="8" />
      <path d="M21 21l-4.35-4.35" />
    </>
  ),
  arrowRight: (
    <>
      <path d="M5 12h14M12 5l7 7-7 7" />
    </>
  ),
  check: (
    <>
      <path d="M20 6L9 17l-5-5" />
    </>
  ),
  plus: (
    <>
      <path d="M12 5v14M5 12h14" />
    </>
  ),
  creditCard: (
    <>
      <rect x="1" y="4" width="22" height="16" rx="2" />
      <path d="M1 10h22" />
    </>
  ),
  image: (
    <>
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <circle cx="8.5" cy="8.5" r="1.5" />
      <path d="M21 15l-5-5L5 21" />
    </>
  ),
  palette: (
    <>
      <circle cx="13.5" cy="6.5" r="2" />
      <circle cx="17.5" cy="10.5" r="2" />
      <circle cx="8.5" cy="7.5" r="2" />
      <circle cx="6.5" cy="12.5" r="2" />
      <path d="M12 2C6.49 2 2 6.49 2 12s4.49 10 10 10c.55 0 1-.45 1-1v-1.5c0-.28.22-.5.5-.5H16a4 4 0 004-4c0-5.04-3.58-9-8-9z" />
    </>
  ),
  frame: (
    <>
      <rect x="2" y="2" width="20" height="20" rx="2" />
      <path d="M2 7h20M7 2v20" />
    </>
  ),
  verified: (
    <>
      <path d="M12 2l2.4 3.6L18 4l-1.2 3.6L20 10l-3.6.8L16 14.4 12 12l-4 2.4-.4-3.6L4 10l3.2-2.4L6 4l3.6 1.6L12 2z" fill="currentColor" stroke="none" />
      <path d="M9 12l2 2 4-4" stroke="white" strokeWidth="2" />
    </>
  ),
  mapPin: (
    <>
      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" />
      <circle cx="12" cy="10" r="3" />
    </>
  ),
  users: (
    <>
      <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2" />
      <circle cx="9" cy="7" r="4" />
      <path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75" />
    </>
  ),
  shoppingBag: (
    <>
      <path d="M6 2L3 6v14a2 2 0 002 2h14a2 2 0 002-2V6l-3-4zM3 6h18M16 10a4 4 0 01-8 0" />
    </>
  ),
} as const;

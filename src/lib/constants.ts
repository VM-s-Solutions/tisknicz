export const PLATFORM_FEE_RATE = 0.15;

export const PLATFORM_NAME = 'Makables';

export const ORDER_STATUSES = {
  pending_payment: 'Čeká na platbu',
  paid: 'Zaplaceno',
  accepted: 'Přijato',
  shipped: 'Odesláno',
  delivered: 'Doručeno',
  completed: 'Dokončeno',
  cancelled: 'Zrušeno',
  refunded: 'Vráceno',
  disputed: 'Reklamace',
} as const;

export const PRICE_TYPES = {
  fixed: 'Pevná cena',
  from: 'Cena od',
  on_request: 'Na dotaz',
} as const;

export const ALLOWED_FILE_TYPES = [
  'image/jpeg',
  'image/png',
  'image/webp',
  'application/pdf',
  'model/stl',
  'model/3mf',
  'model/obj',
] as const;

export const MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

export const SEED_CATEGORIES = [
  { name: '3D tisk', slug: '3d-tisk', icon: '🖨️', description: 'FDM, SLA, resin tisk na zakázku', sort_order: 1 },
  { name: 'Klasický tisk', slug: 'klasicky-tisk', icon: '📄', description: 'Vizitky, letáky, brožury, plakáty', sort_order: 2 },
  { name: 'Potisk textilu', slug: 'potisk-textilu', icon: '👕', description: 'DTF, DTG, sítotisk, sublimace', sort_order: 3 },
  { name: 'Laser & CNC', slug: 'laser-cnc', icon: '⚡', description: 'Gravírování, řezání, frézování', sort_order: 4 },
  { name: 'Velkoformát', slug: 'velkoformat', icon: '🖼️', description: 'Bannery, rollupy, samolepky, polepy', sort_order: 5 },
  { name: 'Handmade & Kreativní', slug: 'handmade', icon: '🎨', description: 'Originální výrobky, dekorace, dárky', sort_order: 6 },
] as const;

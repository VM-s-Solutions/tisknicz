# Tiskni.cz — Projektová vize

> Dokument pro onboarding kolegů. Popisuje co Tiskni.cz je, proč existuje, kam směřuje a jak je technicky postavený.

---

## Co je Tiskni.cz

**Marketplace pro tiskaře a makery v ČR.** Propojujeme zákazníky, kteří potřebují něco vytisknout (3D tisk, textil, vizitky, gravírování...) s lokálními makery, kteří to umí vyrobit.

Zákazník najde tiskaře ve svém okolí, objedná, zaplatí online. Tiskař vyrobí a odešle přes Zásilkovnu. Platforma běží autonomně — escrow platby, automatická fakturace, provize 15 %.

**Jedna věta:** *Etsy pro české tiskaře.*

---

## Proč to děláme

1. **Trh existuje, ale je roztříštěný** — tisíce makerů v ČR nabízí přes Facebook skupiny, Bazos, Instagram. Žádná jednotná platforma.
2. **Zákazníci nemají jak porovnat** — hledání tiskaře dnes znamená googlit, psát emaily, čekat na odpovědi. Marketplace tohle řeší.
3. **Makeři chtějí prodávat, ne marketovat** — nabízíme hotovou infrastrukturu: profil, katalog, platby, doprava, fakturace.
4. **Nízké provozní náklady** — platforma po rozběhnutí běží téměř sama (automatické platby, faktury, payouty).

---

## 6 kategorií služeb

| Kategorie | Co zahrnuje | Příklad |
|-----------|------------|---------|
| **3D tisk** | FDM, SLA, resin tisk na zakázku | Prototyp dílu, figurka, funkční součástka |
| **Klasický tisk** | Vizitky, letáky, brožury, plakáty | 1000 ks vizitek, A3 plakát |
| **Potisk textilu** | DTF, DTG, sítotisk, sublimace | Tričko s vlastním designem |
| **Laser & CNC** | Gravírování, řezání, frézování | Gravírované dřevěné prkénko |
| **Velkoformát** | Bannery, rollupy, samolepky, polepy | Roll-up banner 85x200cm |
| **Handmade** | Originální výrobky, dekorace, dárky | Pryskyřicový přívěsek na míru |

---

## Jak to funguje — objednávkový flow

```
ZÁKAZNÍK                    PLATFORMA                    MAKER
   │                            │                           │
   ├── Vybere tiskaře ────────>│                           │
   ├── Vybere produkt          │                           │
   ├── Vyplní formulář:        │                           │
   │   - jméno, email, telefon │                           │
   │   - vybere Zásilkovnu     │                           │
   │   - upload soubory        │                           │
   │                           │                           │
   ├── Klikne "Zaplatit" ────>│                           │
   │                           ├── Vytvoří objednávku      │
   │                           ├── Přesměruje na Comgate   │
   │<── Zaplatí na Comgate ───│                           │
   │                           │                           │
   │    Comgate webhook ──────>│                           │
   │                           ├── Status → 'zaplaceno'    │
   │                           ├── Vygeneruje fakturu      │
   │                           ├── Email zákazníkovi       │
   │                           ├── Email makerovi ────────>│
   │                           │                           │
   │                           │         Maker klikne "Přijmout"
   │                           │<─── Přijato ─────────────│
   │                           │         Maker vyrobí...   │
   │                           │         Maker klikne "Odesláno"
   │                           │<─── Odesláno ────────────│
   │                           ├── Zásilkovna: vytvoří balík│
   │                           ├── Email s trackem ────────>│
   │                           │                           │
   │── "Převzato" ───────────>│  (nebo auto po 7 dnech)   │
   │                           ├── Status → 'doručeno'     │
   │                           │                           │
   │    === VÝPLATA (1x týdně) │                           │
   │                           ├── Admin spustí payout     │
   │                           ├── Generuje fakturu provize│
   │                           ├── CSV export pro banku ──>│
   │                           ├── Status → 'dokončeno'    │
```

---

## Byznys model

| Metrika | Hodnota |
|---------|---------|
| **Provize** | 15 % z ceny produktu (ne z dopravy) |
| **Doprava** | Hradí zákazník (Zásilkovna ~69-89 Kč) |
| **Výplaty makerům** | 1x týdně, hromadný bankovní převod |
| **Faktury** | Automaticky: zákazníkovi při platbě, makerovi při výplatě |
| **Registrace makera** | Zdarma, potřebuje jen IČO |
| **Minimální objednávka** | Žádná (záleží na makerovi) |

**Příklad kalkulace:**
- Zákazník zaplatí: produkt 500 Kč + doprava 79 Kč = **579 Kč**
- Provize platformy: 500 × 15 % = **75 Kč**
- Maker dostane: 500 - 75 + 79 = **504 Kč**

---

## Tech stack

| Vrstva | Technologie |
|--------|------------|
| **Framework** | Next.js 16 (App Router, Server Components, TypeScript) |
| **Styling** | Tailwind CSS 4 (teal/tyrkysová paleta) |
| **Databáze** | PostgreSQL via Supabase |
| **Auth** | Supabase Auth (email + heslo, magic link) |
| **Platby** | Comgate API (česká platební brána — karty, bankovní převody) |
| **Doprava** | Zásilkovna / Packeta API (widget + REST) |
| **Soubory** | Supabase Storage (fotky, STL, PDF) |
| **Email** | Resend (transakční emaily) |
| **Faktury** | @react-pdf/renderer (PDF generace) |
| **ARES** | Automatické načtení firmy z IČO |
| **3D vizuál** | React Three Fiber + Three.js (hero animace) |
| **Deploy** | Vercel (frontend) + Supabase (backend) |

---

## Struktura projektu

```
src/
├── app/                           # Next.js stránky a API
│   ├── page.tsx                   # Landing page (3D hero, kategorie, CTA)
│   ├── katalog/
│   │   ├── page.tsx               # Seznam makerů s filtry
│   │   └── [slug]/page.tsx        # Detail makera + produkty
│   ├── auth/
│   │   ├── login/page.tsx         # Přihlášení (split layout)
│   │   └── register/page.tsx      # Registrace zákazník/maker
│   ├── dashboard/
│   │   ├── maker/                 # Dashboard makera (objednávky, produkty, profil)
│   │   ├── zakaznik/              # Dashboard zákazníka
│   │   └── admin/                 # Admin panel (zatím nerealizován)
│   └── api/                       # REST endpointy
│       ├── ares/[ico]/            # ARES validace IČO
│       ├── makers/                # CRUD makery
│       └── products/              # CRUD produkty
│
├── components/
│   ├── ui/                        # Základní UI: Button, Input, Card, Badge, Alert...
│   ├── layout/                    # Header, Footer
│   ├── catalog/                   # MakerCard, CategoryFilter, CitySearch
│   ├── dashboard/                 # ProductActions
│   ├── forms/                     # MakerRegistrationForm, ProductForm
│   └── shared/                    # HeroScene (3D), HeroSceneWrapper
│
├── lib/
│   ├── supabase/                  # Klienty: server, browser, admin
│   ├── ares/client.ts             # ARES API wrapper
│   ├── demo-data.ts               # Demo data (9 makerů, 8 produktů)
│   ├── constants.ts               # Platformní konstanty
│   └── utils/                     # pricing, validation, dates
│
├── types/                         # TypeScript typy
└── middleware.ts                   # Auth middleware pro /dashboard/*
```

---

## Co je hotové (stav k květnu 2026)

### Hotovo
- [x] Landing page s 3D animovanou hero sekcí (Three.js torus knot, gear, particles)
- [x] Kompletní redesign na teal/tyrkysovou paletu (moderní bold styl)
- [x] Header s navigací + backdrop blur
- [x] Footer (dark theme)
- [x] Katalog makerů — filtrování dle kategorie a města
- [x] MakerCard s tealovým gradientem, hvězdičkami, verified badge
- [x] Detail makera s produkty
- [x] Auth stránky (login + register) — split layout design
- [x] Dashboard maker (přehled, produkty, profil)
- [x] Dashboard zákazník
- [x] Formulář registrace makera s ARES validací IČO
- [x] Formulář přidání produktu
- [x] UI knihovna (Button, Input, Card, Badge, Alert, Icon, Spinner, Select, Textarea)
- [x] Demo data fallback (katalog funguje i bez Supabase)
- [x] Supabase klienty (server, browser, admin)
- [x] API routes (ARES, makers, products)
- [x] Middleware pro ochranu /dashboard routes
- [x] TypeScript strict mode, čistý build bez chyb

### Zbývá dodělat
- [ ] **Comgate integrace** — platební brána (vytvoření platby, webhook, verifikace)
- [ ] **Zásilkovna integrace** — widget pro výběr pobočky + vytvoření zásilky
- [ ] **Objednávkový flow** — formulář objednávky, potvrzení, status tracking
- [ ] **Fakturace** — PDF generátor faktur (@react-pdf/renderer)
- [ ] **Emaily** — Resend šablony (10 typů notifikací)
- [ ] **Payout systém** — týdenní výplaty, CSV export pro banku
- [ ] **Recenze** — zákazník hodnotí makera po doručení
- [ ] **Zprávy** — chat mezi zákazníkem a makerem ke konkrétní objednávce
- [ ] **Admin dashboard** — přehled objednávek, ověřování makerů, statistiky
- [ ] **Auto-deliver cron** — automatické dokončení 7 dní po odeslání
- [ ] **Stránka /pro-tiskare** — průvodce pro nové makery (IČO, daně, registrace)
- [ ] **Stránka /jak-to-funguje** — jak platforma funguje
- [ ] **VOP + GDPR** — právní stránky
- [ ] **SEO** — meta tagy, sitemap, Open Graph
- [ ] **Real Supabase** — nasadit skutečný Supabase projekt s DB schématem a RLS
- [ ] **Vercel deploy** — produkční nasazení

---

## Designový jazyk

**Styl:** Moderní, bold — inspirace Vercel, Linear, Stripe

**Primární barva:** Teal (#0d9488 → #14b8a6 → #2dd4bf)

**Akcent:** Amber (#f59e0b) pro CTA kontrasty

**Pozadí:** Bílé sekce střídané se zinc-50, dark hero (zinc-950)

**Typografie:** Inter, velké bold headings, tighter tracking

**Efekty:**
- Gradient teal buttony s glow efektem
- Hover: scale(1.02) + shadow zvětšení
- Backdrop-blur na sticky headeru
- Dot-pattern pozadí na CTA sekcích
- 3D animace (Three.js) v hero sekci — wireframe torus knot, ozubené kolo, plovoucí prstence, částice

---

## Databázové tabulky

| Tabulka | Účel |
|---------|------|
| `profiles` | Rozšíření Supabase Auth — jméno, telefon, role (customer/maker/admin) |
| `makers` | Profil makera — IČO, firma, adresa, bio, bankovní účet, statistiky |
| `categories` | 6 kategorií služeb |
| `maker_categories` | M:N vazba maker ↔ kategorie |
| `products` | Produkty/služby makera — název, popis, cena, obrázky |
| `orders` | Objednávky — kompletní lifecycle od platby po doručení |
| `reviews` | Hodnocení makera zákazníkem (1-5 hvězd + komentář) |
| `invoices` | Automaticky generované faktury (zákazník + provize) |
| `payout_batches` | Týdenní dávky výplat makerům |
| `messages` | Zprávy k objednávce (zákazník ↔ maker) |

Kompletní SQL schéma viz `TISKNI_MVP_SPEC.md` sekce 2.

---

## Integrace třetích stran

### Comgate (platby)
- Česká platební brána — karty Visa/MC, bankovní převody
- Flow: vytvoříme platbu → redirect na Comgate → zákazník zaplatí → webhook zpět
- Test mode pro vývoj, produkce vyžaduje ověření e-shopu (~14 dní)

### Zásilkovna / Packeta (doprava)
- Frontend: widget pro výběr výdejního místa (Z-Point, Z-Box)
- Backend: API pro vytvoření zásilky a generování štítku
- Vše jede pod naším účtem — makeři nepotřebují vlastní Zásilkovnu

### ARES (registr firem)
- Automatické načtení dat firmy z IČO (název, adresa, DIČ, právní forma)
- Rate limit: max 10 req/min
- Cache 24h (data se nemění často)

### Resend (emaily)
- 10 typů transakčních emailů (nová objednávka, odesláno, výplata...)
- React Email šablony
- Free tier stačí do 3000 emailů/měsíc

---

## Provozní náklady (po launchi)

| Služba | Měsíční náklad |
|--------|---------------|
| Supabase (Free → Pro) | 0 – 600 Kč |
| Vercel (Hobby → Pro) | 0 – 500 Kč |
| Resend | 0 Kč (free tier) |
| Comgate | ~1.5-2.9 % z transakcí |
| Doména tiskni.cz | ~25 Kč/měsíc (300 Kč/rok) |
| **Celkem MVP** | **~0 – 1 000 Kč/měsíc** |

---

## Implementační fáze

### Fáze 1 — Core (hotovo)
Supabase setup, landing page, auth, registrace makera + ARES, katalog, produkty CRUD, UI knihovna, redesign

### Fáze 2 — Objednávky (další krok)
Objednávkový formulář, Zásilkovna widget, Comgate platby, maker dashboard (přijmout, odeslat), zákaznický tracking, zprávy

### Fáze 3 — Business logika
Fakturace (PDF), payout batches + CSV export, auto-deliver cron, recenze + hodnocení

### Fáze 4 — Polish
Email notifikace (Resend), admin dashboard, SEO, /pro-tiskare průvodce, VOP + GDPR

---

## Jak spustit lokálně

```bash
# Naklonovat repozitář
cd tisknicz

# Nainstalovat závislosti
npm install

# Spustit dev server
npm run dev

# Otevřít v prohlížeči
http://localhost:3000
```

Katalog zobrazí demo data (9 makerů, 8 produktů) i bez připojeného Supabase.

Pro plnou funkcionalitu je potřeba:
1. Vytvořit Supabase projekt a nasadit DB schéma
2. Nastavit `.env.local` s reálnými klíči
3. Registrovat se u Comgate a Zásilkovny

---

## Klíčové soubory k nastudování

| Soubor | Co v něm najdeš |
|--------|-----------------|
| `TISKNI_MVP_SPEC.md` | Kompletní technická specifikace (DB schéma, API routes, flows, integrace) |
| `CLAUDE.md` | Pravidla pro AI asistenta (architektura, konvence, code quality) |
| `src/lib/demo-data.ts` | Demo data pro vývoj bez Supabase |
| `src/app/page.tsx` | Landing page — vstupní bod aplikace |
| `src/app/globals.css` | Design tokeny (barevná paleta, animace, efekty) |
| `src/components/ui/` | Základní UI komponenty |
| `src/lib/supabase/` | Supabase klienty (server, browser, admin) |
| `src/lib/utils/pricing.ts` | Kalkulace provize a výplat |

---

*Poslední aktualizace: květen 2026*

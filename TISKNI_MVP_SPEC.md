# MAKABLES (formerly Tiskni.cz) — MVP Specifikace pro Claude Code

Prodejní marketplace portál pro makery v ČR — **Makables** ("Where Ideas Take Shape."). Zákazník najde lokálního makera, objedná, zaplatí. Maker vyrobí, odešle přes Zásilkovnu. Platforma běží téměř autonomně — escrow platby, automatická fakturace, provize. Domain: **makables.cz**, provozovatel: JVM YORE s.r.o.

---

## 1\. TECH STACK

Frontend:  Next.js 14+ (App Router, Server Components, TypeScript)

Styling:   Tailwind CSS

Backend:   Next.js API Routes (Route Handlers)

Database:  PostgreSQL (Supabase hosted — auth \+ DB \+ storage)

Auth:      Supabase Auth (email \+ password, magic link)

Payments:  Comgate API (CZ platební brána — karty, bankovní tlačítka)

Shipping:  Zásilkovna / Packeta API (widget \+ packet creation)

Files:     Supabase Storage (fotky výrobků, upload STL/PDF souborů)

Invoicing: Vlastní generátor (PDF faktury přes @react-pdf/renderer)

ARES:      https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{ICO}

Email:     Resend (transakční emaily — potvrzení objednávky, notifikace)

Deploy:    Vercel (frontend) \+ Supabase (backend services)

---

## 2\. DATABÁZOVÉ SCHÉMA (PostgreSQL / Supabase)

\-- \============================================

\-- USERS (Supabase Auth handles auth, this extends it)

\-- \============================================

CREATE TABLE profiles (

  id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,

  email TEXT NOT NULL,

  full\_name TEXT NOT NULL,

  phone TEXT,

  role TEXT NOT NULL CHECK (role IN ('customer', 'maker', 'admin')) DEFAULT 'customer',

  avatar\_url TEXT,

  created\_at TIMESTAMPTZ DEFAULT NOW(),

  updated\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- MAKERS (extends profile for makers)

\-- \============================================

CREATE TABLE makers (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  user\_id UUID NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,

  \-- ARES data (auto-fetched)

  ico TEXT NOT NULL UNIQUE,

  dic TEXT, \-- nullable, not everyone is VAT payer

  company\_name TEXT NOT NULL,

  legal\_form TEXT, \-- s.r.o., OSVČ etc.

  \-- Address

  street TEXT NOT NULL,

  city TEXT NOT NULL,

  zip TEXT NOT NULL,

  \-- Business details

  bio TEXT, \-- about the maker, max 500 chars

  website TEXT,

  \-- Bank account for payouts

  bank\_account TEXT NOT NULL, \-- Czech format: 123456789/0100

  \-- Settings

  accepts\_custom\_orders BOOLEAN DEFAULT true,

  personal\_pickup BOOLEAN DEFAULT false,

  pickup\_address TEXT,

  pickup\_note TEXT, \-- "Po-Pá 9-17, nebo po domluvě"

  \-- Status

  is\_verified BOOLEAN DEFAULT false,

  is\_active BOOLEAN DEFAULT true,

  \-- Stats (denormalized for performance)

  rating\_avg DECIMAL(2,1) DEFAULT 0,

  rating\_count INTEGER DEFAULT 0,

  total\_orders INTEGER DEFAULT 0,

  total\_revenue INTEGER DEFAULT 0, \-- in CZK, cumulative

  \--

  created\_at TIMESTAMPTZ DEFAULT NOW(),

  updated\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- MAKER CATEGORIES (what they offer)

\-- \============================================

CREATE TABLE categories (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  name TEXT NOT NULL, \-- "3D tisk", "Potisk textilu", etc.

  slug TEXT NOT NULL UNIQUE,

  icon TEXT, \-- emoji or icon name

  description TEXT,

  sort\_order INTEGER DEFAULT 0

);

\-- Seed categories:

\-- 3d-tisk, klasicky-tisk, potisk-textilu, laser-cnc, velkoformat, handmade

CREATE TABLE maker\_categories (

  maker\_id UUID REFERENCES makers(id) ON DELETE CASCADE,

  category\_id UUID REFERENCES categories(id) ON DELETE CASCADE,

  PRIMARY KEY (maker\_id, category\_id)

);

\-- \============================================

\-- PRODUCTS / SERVICES (maker's offerings)

\-- \============================================

CREATE TABLE products (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  maker\_id UUID NOT NULL REFERENCES makers(id) ON DELETE CASCADE,

  category\_id UUID REFERENCES categories(id),

  \-- Product info

  title TEXT NOT NULL,

  description TEXT,

  price INTEGER NOT NULL, \-- in CZK (haléře not needed for MVP)

  price\_type TEXT NOT NULL CHECK (price\_type IN ('fixed', 'from', 'on\_request')) DEFAULT 'fixed',

  \-- "fixed" \= exact price, "from" \= starting at, "on\_request" \= contact maker

  \-- Media

  images TEXT\[\] DEFAULT '{}', \-- array of Supabase Storage URLs

  \-- Shipping

  weight\_grams INTEGER, \-- for Zásilkovna

  \-- Status

  is\_active BOOLEAN DEFAULT true,

  \--

  created\_at TIMESTAMPTZ DEFAULT NOW(),

  updated\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- ORDERS

\-- \============================================

CREATE TABLE orders (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  order\_number TEXT NOT NULL UNIQUE, \-- human-readable: T-20260001

  \-- Parties

  customer\_id UUID NOT NULL REFERENCES profiles(id),

  maker\_id UUID NOT NULL REFERENCES makers(id),

  product\_id UUID REFERENCES products(id), \-- nullable for custom orders

  \-- Order details

  title TEXT NOT NULL, \-- what is being ordered

  description TEXT, \-- customer's requirements

  quantity INTEGER DEFAULT 1,

  \-- Files (customer uploads: STL, PDF, images)

  attachments TEXT\[\] DEFAULT '{}',

  \-- Pricing

  product\_price INTEGER NOT NULL, \-- price of the product/service in CZK

  shipping\_price INTEGER NOT NULL DEFAULT 0, \-- shipping cost in CZK

  platform\_fee INTEGER NOT NULL, \-- 15% of product\_price

  maker\_payout INTEGER NOT NULL, \-- product\_price \- platform\_fee \+ shipping\_price

  total\_price INTEGER NOT NULL, \-- product\_price \+ shipping\_price (what customer pays)

  \-- Shipping

  shipping\_method TEXT NOT NULL CHECK (shipping\_method IN ('zasilkovna', 'personal\_pickup')),

  zasilkovna\_branch\_id TEXT, \-- Zásilkovna pickup point ID

  zasilkovna\_branch\_name TEXT,

  zasilkovna\_packet\_id TEXT, \-- after packet creation via API

  zasilkovna\_tracking\_url TEXT,

  \-- Customer address (for label)

  customer\_name TEXT NOT NULL,

  customer\_email TEXT NOT NULL,

  customer\_phone TEXT NOT NULL,

  \-- Status flow

  status TEXT NOT NULL CHECK (status IN (

    'pending\_payment',   \-- waiting for Comgate payment

    'paid',              \-- payment received, waiting for maker to accept

    'accepted',          \-- maker accepted, working on it

    'shipped',           \-- maker shipped via Zásilkovna

    'delivered',         \-- customer confirmed receipt (or auto after 7 days)

    'completed',         \-- payout sent to maker

    'cancelled',         \-- cancelled by customer or maker

    'refunded',          \-- money returned to customer

    'disputed'           \-- customer opened a dispute

  )) DEFAULT 'pending\_payment',

  \-- Payment

  comgate\_transaction\_id TEXT,

  payment\_method TEXT, \-- 'card', 'bank\_transfer', etc.

  paid\_at TIMESTAMPTZ,

  \-- Payout

  payout\_batch\_id UUID REFERENCES payout\_batches(id),

  paid\_out\_at TIMESTAMPTZ,

  \-- Timestamps

  accepted\_at TIMESTAMPTZ,

  shipped\_at TIMESTAMPTZ,

  delivered\_at TIMESTAMPTZ,

  cancelled\_at TIMESTAMPTZ,

  auto\_deliver\_at TIMESTAMPTZ, \-- shipped\_at \+ 7 days

  \--

  created\_at TIMESTAMPTZ DEFAULT NOW(),

  updated\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- REVIEWS

\-- \============================================

CREATE TABLE reviews (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  order\_id UUID NOT NULL REFERENCES orders(id) UNIQUE,

  customer\_id UUID NOT NULL REFERENCES profiles(id),

  maker\_id UUID NOT NULL REFERENCES makers(id),

  rating INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),

  comment TEXT,

  maker\_reply TEXT,

  created\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- INVOICES (auto-generated)

\-- \============================================

CREATE TABLE invoices (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  order\_id UUID NOT NULL REFERENCES orders(id),

  invoice\_number TEXT NOT NULL UNIQUE, \-- FV-20260001

  \-- Type: 'customer' \= platform → customer, 'maker' \= maker → platform (for payout), 'fee' \= platform → maker (provize)

  type TEXT NOT NULL CHECK (type IN ('customer', 'fee')),

  \-- Parties

  issuer\_name TEXT NOT NULL,

  issuer\_ico TEXT,

  issuer\_dic TEXT,

  issuer\_address TEXT NOT NULL,

  recipient\_name TEXT NOT NULL,

  recipient\_ico TEXT,

  recipient\_dic TEXT,

  recipient\_address TEXT NOT NULL,

  \-- Amounts

  amount\_without\_vat INTEGER NOT NULL,

  vat\_rate INTEGER DEFAULT 0, \-- 0 or 21

  vat\_amount INTEGER DEFAULT 0,

  amount\_with\_vat INTEGER NOT NULL,

  \-- Metadata

  description TEXT NOT NULL,

  issue\_date DATE NOT NULL,

  due\_date DATE NOT NULL,

  pdf\_url TEXT, \-- Supabase Storage URL

  \--

  created\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- PAYOUT BATCHES (weekly payouts to makers)

\-- \============================================

CREATE TABLE payout\_batches (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  batch\_number TEXT NOT NULL UNIQUE, \-- VYP-2026-W01

  status TEXT NOT NULL CHECK (status IN ('pending', 'processing', 'completed')) DEFAULT 'pending',

  total\_amount INTEGER NOT NULL DEFAULT 0,

  order\_count INTEGER NOT NULL DEFAULT 0,

  \-- Admin generates this, exports CSV for bank transfer

  csv\_url TEXT, \-- download link for bank batch transfer

  processed\_at TIMESTAMPTZ,

  created\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- MESSAGES (simple order-related messaging)

\-- \============================================

CREATE TABLE messages (

  id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

  order\_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,

  sender\_id UUID NOT NULL REFERENCES profiles(id),

  content TEXT NOT NULL,

  created\_at TIMESTAMPTZ DEFAULT NOW()

);

\-- \============================================

\-- INDEXES

\-- \============================================

CREATE INDEX idx\_makers\_city ON makers(city);

CREATE INDEX idx\_makers\_active ON makers(is\_active) WHERE is\_active \= true;

CREATE INDEX idx\_products\_maker ON products(maker\_id);

CREATE INDEX idx\_products\_category ON products(category\_id);

CREATE INDEX idx\_products\_active ON products(is\_active) WHERE is\_active \= true;

CREATE INDEX idx\_orders\_customer ON orders(customer\_id);

CREATE INDEX idx\_orders\_maker ON orders(maker\_id);

CREATE INDEX idx\_orders\_status ON orders(status);

CREATE INDEX idx\_reviews\_maker ON reviews(maker\_id);

CREATE INDEX idx\_messages\_order ON messages(order\_id);

---

## 3\. STRÁNKY A ROUTES

app/

├── page.tsx                          \# Landing page (hero, jak to funguje, kategorie, CTA)

├── katalog/

│   ├── page.tsx                      \# Katalog makerů (filtry: kategorie, město, hodnocení)

│   └── \[slug\]/

│       └── page.tsx                  \# Profil makera (bio, produkty, recenze, hodnocení)

├── produkt/

│   └── \[id\]/

│       └── page.tsx                  \# Detail produktu (fotky, popis, cena, objednat)

├── objednavka/

│   ├── page.tsx                      \# Objednávkový formulář (produkt \+ doprava \+ platba)

│   ├── potvrzeni/page.tsx            \# Potvrzení po platbě

│   └── \[id\]/page.tsx                 \# Detail objednávky (status tracking)

├── auth/

│   ├── login/page.tsx                \# Přihlášení

│   └── register/page.tsx             \# Registrace (zákazník nebo maker)

├── dashboard/

│   ├── page.tsx                      \# Dashboard redirect based on role

│   ├── zakaznik/

│   │   ├── page.tsx                  \# Moje objednávky

│   │   └── objednavka/\[id\]/page.tsx  \# Detail objednávky \+ zprávy \+ recenze

│   ├── maker/

│   │   ├── page.tsx                  \# Přehled (nové objednávky, statistiky)

│   │   ├── objednavky/page.tsx       \# Seznam objednávek

│   │   ├── objednavka/\[id\]/page.tsx  \# Detail objednávky (accept, ship, messages)

│   │   ├── produkty/page.tsx         \# Moje produkty (CRUD)

│   │   ├── profil/page.tsx           \# Můj profil / nastavení

│   │   └── vyplaty/page.tsx          \# Přehled výplat a faktur

│   └── admin/

│       ├── page.tsx                  \# Admin dashboard (statistiky)

│       ├── objednavky/page.tsx       \# Všechny objednávky

│       ├── makeri/page.tsx           \# Seznam makerů (verify, deactivate)

│       ├── vyplaty/page.tsx          \# Payout batches (generate, export CSV)

│       └── faktury/page.tsx          \# Přehled faktur

├── jak-to-funguje/page.tsx           \# How it works page

├── pro-tiskare/page.tsx              \# Landing pro makery (jak se registrovat, průvodce)

├── vop/page.tsx                      \# Obchodní podmínky

└── gdpr/page.tsx                     \# Ochrana osobních údajů

---

## 4\. API ROUTES

app/api/

├── auth/

│   └── callback/route.ts             \# Supabase auth callback

├── ares/

│   └── \[ico\]/route.ts                \# GET — fetch company data from ARES

├── makers/

│   ├── route.ts                      \# GET (list+filter), POST (register)

│   └── \[id\]/route.ts                 \# GET, PATCH (update profile)

├── products/

│   ├── route.ts                      \# GET (list+filter), POST (create)

│   └── \[id\]/route.ts                 \# GET, PATCH, DELETE

├── orders/

│   ├── route.ts                      \# GET (list), POST (create order)

│   ├── \[id\]/

│   │   ├── route.ts                  \# GET, PATCH (status updates)

│   │   ├── accept/route.ts           \# POST — maker accepts order

│   │   ├── ship/route.ts             \# POST — maker marks as shipped \+ creates Zásilkovna packet

│   │   ├── deliver/route.ts          \# POST — customer confirms delivery

│   │   └── messages/route.ts         \# GET, POST — order messages

├── payments/

│   ├── create/route.ts               \# POST — create Comgate payment

│   ├── callback/route.ts             \# POST — Comgate webhook (payment status)

│   └── status/\[transId\]/route.ts     \# GET — check payment status

├── zasilkovna/

│   ├── branches/route.ts             \# GET — list pickup points (proxy/cache)

│   └── create-packet/route.ts        \# POST — create Zásilkovna packet for order

├── invoices/

│   ├── route.ts                      \# GET (list)

│   └── \[id\]/pdf/route.ts             \# GET — generate and return PDF

├── payouts/

│   ├── route.ts                      \# GET (list batches), POST (create batch)

│   └── \[id\]/

│       ├── route.ts                  \# GET batch detail

│       └── csv/route.ts              \# GET — export CSV for bank transfer

├── reviews/

│   └── route.ts                      \# POST (create), GET (list for maker)

├── upload/

│   └── route.ts                      \# POST — upload files to Supabase Storage

└── cron/

    └── auto-deliver/route.ts         \# POST — auto-deliver orders after 7 days (Vercel Cron)

---

## 5\. KLÍČOVÉ FLOWS — DETAIL

### 5.1 REGISTRACE MAKERA

1\. User klikne "Registrovat se jako tiskař" na /pro-tiskare

2\. Vyplní email \+ heslo → Supabase Auth vytvoří účet (role: maker)

3\. Formulář "Dokončení profilu makera":

   a. Zadá IČO

   b. Frontend volá GET /api/ares/{ico}

   c. ARES vrátí: company\_name, street, city, zip, legal\_form, dic

   d. Formulář se předvyplní daty z ARES (user může opravit adresu)

   e. User doplní: bio, telefon, bankovní účet, kategorie, zda nabízí osobní odběr

   f. Nahraje fotky vybavení/výrobků

4\. Submit → POST /api/makers → záznam v DB

5\. Maker je ihned aktivní (is\_verified \= false, ale může přijímat objednávky)

6\. Admin ho může manuálně ověřit (badge "Ověřeno")

**ARES API endpoint:**

GET https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{ICO}

Response (relevant fields):

{

  "ico": "12345678",

  "obchodniJmeno": "Firma s.r.o.",

  "sidlo": {

    "textovaAdresa": "Ulice 123, 110 00 Praha 1",

    "nazevObce": "Praha",

    "psc": 11000,

    "nazevUlice": "Ulice",

    "cisloDomovni": 123

  },

  "pravniForma": "112", // code → map to text

  "dic": "CZ12345678",

  "datumVzniku": "2020-01-15"

}

### 5.2 PRŮVODCE PRO MAKERY (stránka /pro-tiskare)

Obsah stránky — interaktivní checklist / FAQ:

\#\# Jak začít prodávat na Tiskni.cz

\#\#\# 1\. Potřebuješ IČO

\- Nemáš? Založení živnosti volné trvá cca 15 minut online.

\- Jdi na www.rzp.cz → Jednotný registrační formulář (JRF)

\- Vyber "Ohlášení živnosti" → Živnost volná

\- Obor: "Výroba, obchod a služby jinde nezařazené"

\- Poplatek: 1 000 Kč

\- IČO dostaneš do 5 pracovních dnů

\#\#\# 2\. Daně a odvody — neboj se, je to jednoduché

\- Paušální daň: cca 7 500 Kč/měsíc (vše v jednom — daň \+ sociální \+ zdravotní)

\- Platí pokud máš příjem do 2 mil. Kč/rok a nejsi plátce DPH

\- Nemusíš podávat přiznání, nemusíš mít účetního

\- Více: www.financnisprava.cz/pausalni-dan

\#\#\# 3\. Registrace na Tiskni.cz

\- Zadej IČO → data se načtou automaticky z ARES

\- Nastav si profil, kategorie, ceník

\- Nahraj fotky svých výrobků

\- Zadej bankovní účet pro výplaty

\#\#\# 4\. Jak fungují objednávky

\- Zákazník objedná a zaplatí přes portál

\- Ty dostaneš notifikaci emailem

\- Přijmeš objednávku → vyrobíš → odešleš přes Zásilkovnu

\- Po doručení ti vyplatíme peníze (minus 15% provize)

\#\#\# 5\. Jak řešit dopravu (Zásilkovna)

\- Při odeslání ti systém vygeneruje štítek Zásilkovny

\- Vytiskni štítek, nalep na balík

\- Dones na nejbližší Z-Point nebo Z-Box

\- Tracking automaticky sdílíme se zákazníkem

\- Zásilkovna stojí zákazníka cca 69-89 Kč (dle velikosti)

\#\#\# 6\. Jak nacenit výrobky

\- Materiál \+ energie \+ čas \+ marže

\- Příklad 3D tisk: 1kg PLA \= cca 500 Kč, 1h tisku \= cca 50 Kč energie

\- Náš tip: podívej se na ceny konkurence v katalogu

### 5.3 OBJEDNÁVKOVÝ FLOW

ZÁKAZNÍK                        PLATFORMA                       MAKER

    |                               |                              |

    |-- Vybere produkt \------------\>|                              |

    |-- Vyplní formulář:            |                              |

    |   \- jméno, email, telefon     |                              |

    |   \- Zásilkovna widget →       |                              |

    |     vybere výdejní místo      |                              |

    |   \- nebo osobní odběr         |                              |

    |   \- poznámka k objednávce     |                              |

    |   \- upload souborů (STL/PDF)  |                              |

    |                               |                              |

    |-- Klikne "Zaplatit" \--------\>|                              |

    |                               |-- Vytvoří order (pending\_payment)

    |                               |-- POST Comgate: vytvoří platbu

    |\<-- Redirect na Comgate \-------|                              |

    |                               |                              |

    |-- Zaplatí na Comgate \--------\>|                              |

    |                               |                              |

    |   Comgate webhook \-----------\>|                              |

    |                               |-- Status → 'paid'            |

    |                               |-- Generuje fakturu (customer)|

    |                               |-- Email zákazníkovi          |

    |                               |-- Email makerovi \-----------\>|

    |                               |                              |

    |                               |              Maker klikne "Přijmout"

    |                               |\<----- POST /accept \---------|

    |                               |-- Status → 'accepted'       |

    |                               |-- Email zákazníkovi          |

    |                               |                              |

    |                               |              Maker vyrobí    |

    |                               |              Maker klikne "Odesláno"

    |                               |\<----- POST /ship \------------|

    |                               |-- Zásilkovna API: createPacket

    |                               |-- Status → 'shipped'        |

    |                               |-- auto\_deliver\_at \= NOW()+7d|

    |                               |-- Email zákazníkovi (tracking)

    |                               |                              |

    |-- Klikne "Převzato" \--------\>|                              |

    |                               |-- Status → 'delivered'      |

    |                               |-- Zákazník může napsat recenzi

    |                               |                              |

    |   (nebo auto po 7 dnech) \----\>|                              |

    |                               |-- Status → 'delivered'      |

    |                               |                              |

    |   \=== PAYOUT (1x týdně) \===   |                              |

    |                               |-- Admin spustí payout batch  |

    |                               |-- Sesbírá delivered orders   |

    |                               |-- Generuje fee fakturu       |

    |                               |-- Exportuje CSV pro banku \--\>|

    |                               |-- Status → 'completed'      |

    |                               |-- Email makerovi (výplatní list)

### 5.4 PLATBA — COMGATE INTEGRACE

// POST /api/payments/create

// Creates a Comgate payment and returns redirect URL

const COMGATE\_URL \= 'https://payments.comgate.cz/v1.0/create';

async function createPayment(order: Order) {

  const params \= new URLSearchParams({

    merchant: process.env.COMGATE\_MERCHANT\_ID\!,

    price: String(order.total\_price \* 100), // v haléřích

    curr: 'CZK',

    label: \`Objednávka ${order.order\_number}\`,

    refId: order.id,

    email: order.customer\_email,

    prepareOnly: 'true', // get URL, don't redirect

    country: 'CZ',

    lang: 'cs',

    method: 'ALL', // all payment methods

    secret: process.env.COMGATE\_SECRET\!,

  });

  const response \= await fetch(COMGATE\_URL, {

    method: 'POST',

    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },

    body: params.toString(),

  });

  const text \= await response.text();

  // Comgate returns: code=0\&message=OK\&transId=XXXX\&redirect=https://...

  const result \= Object.fromEntries(new URLSearchParams(text));

  if (result.code \=== '0') {

    // Save transId to order

    await updateOrder(order.id, {

      comgate\_transaction\_id: result.transId,

    });

    return { redirectUrl: result.redirect };

  }

  throw new Error(\`Comgate error: ${result.message}\`);

}

// POST /api/payments/callback (webhook from Comgate)

async function handleCallback(req: Request) {

  const body \= await req.formData();

  const transId \= body.get('transId') as string;

  const status \= body.get('status') as string; // 'PAID', 'CANCELLED', 'AUTHORIZED'

  // Verify with Comgate

  const verified \= await verifyPayment(transId);

  if (verified.status \=== 'PAID') {

    const order \= await getOrderByTransId(transId);

    await updateOrder(order.id, {

      status: 'paid',

      paid\_at: new Date(),

      payment\_method: verified.method,

    });

    // Generate customer invoice

    await generateInvoice(order, 'customer');

    // Notify maker

    await sendEmail(order.maker.email, 'new-order', order);

    // Notify customer

    await sendEmail(order.customer\_email, 'order-paid', order);

  }

  return new Response('OK', { status: 200 });

}

**Comgate callback URLs (nastavit v Comgate portálu):**

Pending URL:  https://tiskni.cz/objednavka/potvrzeni?status=pending

Paid URL:     https://tiskni.cz/objednavka/potvrzeni?status=paid

Cancelled URL: https://tiskni.cz/objednavka/potvrzeni?status=cancelled

Status URL:   https://tiskni.cz/api/payments/callback (background webhook)

### 5.5 ZÁSILKOVNA INTEGRACE

**A) Widget pro výběr výdejního místa (frontend):**

\<\!-- V objednávkovém formuláři \--\>

\<script src="https://widget.packeta.com/v6/www/js/library.js"\>\</script\>

\<script\>

  // Otevře Zásilkovna widget pro výběr výdejního místa

  function openPacketaWidget() {

    Packeta.Widget.pick(

      process.env.NEXT\_PUBLIC\_ZASILKOVNA\_API\_KEY,

      handlePickupPointSelected,

      {

        country: 'cz',

        language: 'cs',

        appIdentity: 'tiskni-cz-v1',

      }

    );

  }

  function handlePickupPointSelected(point) {

    if (point) {

      // Uložit do formu:

      // point.id — branch ID

      // point.name — "Zásilkovna \- Praha 2, Vinohradská"

      // point.place — adresa

      setSelectedBranch({

        id: point.id,

        name: point.name,

        address: point.place,

      });

    }

  }

\</script\>

**B) Vytvoření zásilky (backend, při odeslání makerem):**

// POST /api/zasilkovna/create-packet

// Called when maker clicks "Odesláno"

const PACKETA\_API\_URL \= 'https://www.zasilkovna.cz/api/rest';

const PACKETA\_API\_KEY \= process.env.ZASILKOVNA\_API\_KEY\!;

async function createPacketaPacket(order: Order) {

  // Zásilkovna REST API uses XML (SOAP-like)

  // Or use their REST JSON endpoint if available

  const packetData \= {

    apiPassword: PACKETA\_API\_KEY,

    packetAttributes: {

      number: order.order\_number,

      name: order.customer\_name.split(' ')\[0\],

      surname: order.customer\_name.split(' ').slice(1).join(' '),

      email: order.customer\_email,

      phone: order.customer\_phone,

      addressId: order.zasilkovna\_branch\_id, // výdejní místo

      value: order.total\_price, // hodnota zásilky v CZK

      weight: order.product?.weight\_grams

        ? order.product.weight\_grams / 1000 // kg

        : 1, // default 1kg

      eshop: process.env.ZASILKOVNA\_SENDER\_LABEL\!, // "Tiskni.cz"

    },

  };

  // Use Packeta REST API v2

  const response \= await fetch('https://www.zasilkovna.cz/api/rest', {

    method: 'POST',

    headers: { 'Content-Type': 'application/json' },

    body: JSON.stringify({

      apiPassword: PACKETA\_API\_KEY,

      packetAttributes: packetData.packetAttributes,

    }),

  });

  const result \= await response.json();

  // Update order with packet info

  await updateOrder(order.id, {

    zasilkovna\_packet\_id: result.id,

    zasilkovna\_tracking\_url: \`https://tracking.packeta.com/cs/?id=${result.id}\`,

    status: 'shipped',

    shipped\_at: new Date(),

    auto\_deliver\_at: new Date(Date.now() \+ 7 \* 24 \* 60 \* 60 \* 1000), // \+7 days

  });

  return result;

}

**C) Generování štítku:**

// Maker si stáhne PDF štítek z dashboardu

async function getPacketLabel(packetId: string) {

  const response \= await fetch(

    \`https://www.zasilkovna.cz/api/rest/packet/${packetId}/label\`,

    {

      headers: { Authorization: \`Bearer ${PACKETA\_API\_KEY}\` },

    }

  );

  return response; // PDF binary

}

### 5.6 PROVIZE A VÝPLATY

**Výpočet při objednávce:**

function calculateOrderPricing(productPrice: number, shippingPrice: number) {

  const PLATFORM\_FEE\_RATE \= 0.15; // 15%

  const platformFee \= Math.round(productPrice \* PLATFORM\_FEE\_RATE);

  const makerPayout \= productPrice \- platformFee \+ shippingPrice;

  const totalPrice \= productPrice \+ shippingPrice;

  return {

    product\_price: productPrice,   // 500 CZK

    shipping\_price: shippingPrice, // 79 CZK (Zásilkovna)

    platform\_fee: platformFee,     // 75 CZK (15% z 500\)

    maker\_payout: makerPayout,     // 504 CZK (500-75+79)

    total\_price: totalPrice,       // 579 CZK (co platí zákazník)

  };

}

**Payout batch (admin, 1x týdně):**

// POST /api/payouts — admin creates weekly payout batch

async function createPayoutBatch() {

  // 1\. Get all 'delivered' orders not yet paid out

  const orders \= await getOrdersForPayout(); // status='delivered', payout\_batch\_id IS NULL

  // 2\. Group by maker

  const byMaker \= groupBy(orders, 'maker\_id');

  // 3\. Create batch

  const batch \= await createBatch({

    batch\_number: \`VYP-${year}-W${weekNumber}\`,

    total\_amount: sum(orders.map(o \=\> o.maker\_payout)),

    order\_count: orders.length,

  });

  // 4\. Assign orders to batch

  for (const order of orders) {

    await updateOrder(order.id, {

      payout\_batch\_id: batch.id,

      status: 'completed',

      paid\_out\_at: new Date(),

    });

  }

  // 5\. Generate fee invoices (platform → maker) for each maker

  for (const \[makerId, makerOrders\] of Object.entries(byMaker)) {

    const totalFee \= sum(makerOrders.map(o \=\> o.platform\_fee));

    await generateInvoice({

      type: 'fee',

      maker\_id: makerId,

      amount: totalFee,

      orders: makerOrders,

      description: \`Provize za zprostředkování – ${makerOrders.length} objednávek\`,

    });

  }

  // 6\. Generate CSV for bank batch transfer

  const csv \= generateBankCSV(byMaker);

  // CSV format: účet příjemce, částka, variabilní symbol, zpráva

  // Each row \= one maker, sum of all their payouts in this batch

  return { batch, csv };

}

**CSV pro banku (formát pro hromadný příkaz):**

"číslo účtu","částka","variabilní symbol","zpráva pro příjemce"

"123456789/0100","504","20260001","Tiskni.cz výplata KW19/2026"

"987654321/0300","1250","20260002","Tiskni.cz výplata KW19/2026"

### 5.7 AUTOMATICKÁ FAKTURACE

Dvě faktury na objednávku:

**1\) Faktura zákazníkovi** (generuje se při platbě)

Dodavatel:   JVM YORE s.r.o. (IČO, adresa, DIČ pokud plátce)

Odběratel:   Zákazník (jméno, email)

Položka:     \[Název produktu\] — 500 Kč

             Doprava Zásilkovna — 79 Kč

Celkem:      579 Kč

**2\) Faktura za provizi makerovi** (generuje se při payout batchi)

Dodavatel:   JVM YORE s.r.o.

Odběratel:   \[Maker firma\] (IČO, adresa z ARES)

Položka:     Provize za zprostředkování prodeje — 3 objednávky — 225 Kč

Celkem:      225 Kč (nebo \+ DPH pokud jsi plátce)

### 5.8 AUTO-DELIVER CRON

// Vercel Cron: every day at 8:00 AM

// vercel.json: { "crons": \[{ "path": "/api/cron/auto-deliver", "schedule": "0 8 \* \* \*" }\] }

async function autoDeliver() {

  const orders \= await db.query(\`

    SELECT \* FROM orders

    WHERE status \= 'shipped'

    AND auto\_deliver\_at \<= NOW()

  \`);

  for (const order of orders) {

    await updateOrder(order.id, {

      status: 'delivered',

      delivered\_at: new Date(),

    });

    await sendEmail(order.customer\_email, 'auto-delivered', order);

    await sendEmail(order.maker.email, 'order-delivered', order);

  }

}

---

## 6\. ENVIRONMENT VARIABLES

\# Supabase

NEXT\_PUBLIC\_SUPABASE\_URL=https://xxx.supabase.co

NEXT\_PUBLIC\_SUPABASE\_ANON\_KEY=xxx

SUPABASE\_SERVICE\_ROLE\_KEY=xxx

\# Comgate

COMGATE\_MERCHANT\_ID=xxx

COMGATE\_SECRET=xxx

COMGATE\_TEST=true  \# false in production

\# Zásilkovna

NEXT\_PUBLIC\_ZASILKOVNA\_API\_KEY=xxx  \# for widget

ZASILKOVNA\_API\_KEY=xxx              \# for REST API

ZASILKOVNA\_SENDER\_LABEL=tiskni-cz   \# e-shop label in Zásilkovna admin

\# Resend (email)

RESEND\_API\_KEY=xxx

EMAIL\_FROM=objednavky@tiskni.cz

\# Platform config

NEXT\_PUBLIC\_PLATFORM\_NAME=Tiskni.cz

NEXT\_PUBLIC\_PLATFORM\_URL=https://tiskni.cz

PLATFORM\_FEE\_RATE=0.15

PLATFORM\_ICO=xxx          \# JVM YORE IČO

PLATFORM\_DIC=xxx          \# if VAT payer

PLATFORM\_COMPANY=JVM YORE s.r.o.

PLATFORM\_ADDRESS=xxx

\# Vercel Cron secret

CRON\_SECRET=xxx

---

## 7\. EMAILY (Resend templates)

1\. welcome-maker        — Vítej na Tiskni.cz\! Tvůj profil je aktivní.

2\. new-order             — Nová objednávka \#{order\_number} čeká na přijetí\!

3\. order-paid            — Platba přijata. Tvá objednávka je v přípravě.

4\. order-accepted        — Maker přijal objednávku. Pracuje se na tom\!

5\. order-shipped         — Odesláno\! Tracking: {tracking\_url}

6\. order-delivered       — Potvrzeno doručení. Jak jsi spokojen/a?

7\. auto-delivered        — Objednávka automaticky dokončena (7 dní od odeslání).

8\. payout-sent           — Výplata odeslána: {amount} Kč za {count} objednávek.

9\. new-message           — Nová zpráva k objednávce \#{order\_number}.

10\. review-received      — Zákazník ti dal {rating}⭐ recenzi.

---

## 8\. BEZPEČNOSTNÍ OPATŘENÍ

\- Supabase RLS (Row Level Security) na všech tabulkách

  \- Customer vidí jen své objednávky

  \- Maker vidí jen své objednávky a produkty

  \- Admin vidí vše

\- ARES endpoint: rate limiting (max 10 req/min per IP)

\- File upload: max 10MB, povolené typy: jpg, png, webp, pdf, stl, 3mf, obj

\- Comgate webhook: ověření IP adresy Comgate serveru

\- CSRF protection na všech POST routes

\- Input sanitization (XSS prevention)

\- Bankovní účet makera: validace českého formátu (číslo/kód banky)

---

## 9\. SEED DATA (kategorie)

const categories \= \[

  { name: '3D tisk', slug: '3d-tisk', icon: '🖨️', description: 'FDM, SLA, resin tisk na zakázku' },

  { name: 'Klasický tisk', slug: 'klasicky-tisk', icon: '📄', description: 'Vizitky, letáky, brožury, plakáty' },

  { name: 'Potisk textilu', slug: 'potisk-textilu', icon: '👕', description: 'DTF, DTG, sítotisk, sublimace' },

  { name: 'Laser & CNC', slug: 'laser-cnc', icon: '⚡', description: 'Gravírování, řezání, frézování' },

  { name: 'Velkoformát', slug: 'velkoformat', icon: '🖼️', description: 'Bannery, rollupy, samolepky, polepy' },

  { name: 'Handmade & Kreativní', slug: 'handmade', icon: '🎨', description: 'Originální výrobky, dekorace, dárky' },

\];

---

## 10\. DEPLOYMENT CHECKLIST

1\. \[ \] Supabase projekt — vytvořit, nastavit DB schéma, RLS, storage buckets

2\. \[ \] Comgate — registrace, ověření e-shopu (trvá \~14 dní pro karty)

3\. \[ \] Zásilkovna — registrace jako e-shop, získat API klíč a heslo

4\. \[ \] Resend — registrace, verifikace domény tiskni.cz

5\. \[ \] Doména — koupit tiskni.cz (nebo alternativu)

6\. \[ \] Vercel — deploy, nastavit environment variables

7\. \[ \] DNS — nasměrovat doménu na Vercel

8\. \[ \] Testovací objednávka — celý flow od A do Z v testovacím režimu

9\. \[ \] VOP \+ GDPR — napsat a nasadit (šablona Cleansia jako základ)

10.\[ \] 20 makerů — osobně oslovit a registrovat (Praha \+ Brno)

---

## 11\. ODHAD NÁKLADŮ (měsíčně, po launchi)

Supabase (Free tier → Pro)      0 – 25 USD/m

Vercel (Hobby → Pro)            0 – 20 USD/m

Resend (Free tier)               0 USD (do 3K emails/m)

Comgate                          provize cca 1.5-2.9% z transakcí

Doména                           cca 300 Kč/rok

\----------------------------------------------

Total MVP:                       cca 0 – 1000 Kč/m

---

## 12\. PRIORITA IMPLEMENTACE (doporučené pořadí)

FÁZE 1 — Core (1-2 týdny)

  1\. Supabase setup (DB, Auth, Storage)

  2\. Landing page

  3\. Auth (login, register)

  4\. Maker registration \+ ARES

  5\. Product CRUD

  6\. Katalog (listing \+ filter)

FÁZE 2 — Orders (1-2 týdny)

  7\. Order flow (formulář, Zásilkovna widget)

  8\. Comgate platby

  9\. Maker dashboard (accept, ship)

  10\. Customer order tracking

  11\. Messages

FÁZE 3 — Business Logic (1 týden)

  12\. Invoice generation (PDF)

  13\. Payout batches \+ CSV export

  14\. Auto-deliver cron

  15\. Reviews \+ ratings

FÁZE 4 — Polish (1 týden)

  16\. Email notifications (Resend)

  17\. Admin dashboard

  18\. SEO (meta tags, sitemap)

  19\. /pro-tiskare průvodce page

  20\. VOP \+ GDPR pages  

# Revize dev webu — makables-dev-web.azurewebsites.net

**Datum revize:** 4. 7. 2026
**Revidovaná verze:** `origin/master` @ `76ab6e3` (redesign veřejného webu, WidyZz, 2. 7. 2026)
**Metoda:** průchod headless prohlížečem — vizuální kontrola (desktop 1280, mobil 375), funkční testy (navigace, formuláře, odkazy), kontrola konzole a síťových requestů, ověření obsahu proti stavu backendu v repu.
**Prošlé stránky:** homepage, /katalog, /jak-to-funguje, /pro-makery, /vop, /gdpr, /login, /register (+ mobilní menu, footer, CTA odkazy).

---

## Shrnutí

Redesign vypadá profesionálně a drží brand (tmavý vizuál, 3D hero, konzistentní komponenty). Responzivita a chybové stavy jsou zvládnuté. Našly se ale **3 kritické nálezy** — nefunkční backend na dev prostředí, rozpor v komunikované provizi vůči konfiguraci platformy a smyšlené statistiky s právním rizikem — plus řada obsahových a drobných vizuálních vad.

| Závažnost | Počet |
|---|---|
| 🔴 Kritická | 3 |
| 🟠 Obsahová / právní | 4 |
| 🟡 Vizuální / technická | 2 |

---

## 🔴 Kritické nálezy

### K1 — Backend na dev prostředí neběží (503)

- **Projev:** /katalog zobrazí „Katalog se nepodařilo načíst — Server je momentálně nedostupný. Zkuste to prosím znovu." Přihlášení vrátí tutéž chybu. Registrace nepůjde dokončit.
- **Diagnostika:** všechny requesty prohlížeč → web jsou 200 (chyba vzniká při SSR volání backendu). Přímé sondy na API hosty:
  - `makables-dev-public.azurewebsites.net` → **503** na `/`, timeout na `/api/v1/health`
  - `makables-dev-customer` / `makables-dev-maker` → timeout
  - DNS v pořádku (hosty v Azure existují) → **aplikace jsou zastavené, nebo padají při startu**.
- **Dopad:** web je jen fasáda — nelze ověřit katalog, registraci, přihlášení ani objednávkový tok. Blokuje jakékoliv E2E testování.
- **Akce:** Azure portál → App Service *Log stream* / stav aplikací; ověřit, zda backend deploy pipeline vůbec proběhla (frontend evidentně nasazený je, backend možná ne).
- Pozitivum: frontendové chybové stavy fungují vzorově (česká hláška + tlačítko „Zkusit znovu", u loginu červený alert nad formulářem).

### K2 — Provize na webu (7 % → 3,5 %) ≠ konfigurace platformy (15 %)

- **Web komunikuje:** homepage hero statistika „7 % > 3,5 % Provize platformy"; stránka *Pro makery*: „Základní provize je 7 % z ceny produktu… až na 3,5 %. Loajalitu odměňujeme." + příklad kalkulace.
- **Backend má:** `platformFeeRateBp: 1500` (= 15 %) v `backend/src/Makables.Infra.Database/Seeding/CountrySeed.cs:40`.
- **Navíc:** věrnostní (per‑maker) sazba v platformě neexistuje — sazba je jedna na zemi (`CountryConfiguration.PlatformFeeRateBp`). „7 % → 3,5 %" tedy není jen změna čísla, ale nová funkce (fee‑override na makerovi).
- **Akce:** rozhodnout finální sazbu; buď migrace seedu na 700 bp + ticket na věrnostní override, nebo stáhnout čísla z webu. Nesmí se spustit ve stavu, kdy web slibuje 7 % a systém účtuje 15 %.
- Pozn.: samotný příklad kalkulace na *Pro makery* je matematicky správně a sedí na vzorec platformy (1 000 − 70 + 79 = 1 009 Kč při 7 %; 1 044 Kč při 3,5 %). ✓

### K3 — Smyšlené statistiky (právní riziko)

- **Kde:** homepage („250+ Ověřených makerů"), login i registrace („Ověření makeři 250+", „Průměrné hodnocení 4,9/5").
- **Problém:** platforma před launchem nemá žádné makery ani hodnocení. Uvádění smyšlených čísel je klamavá obchodní praktika (zákon o ochraně spotřebitele, dozor ČOI) a podkopává důvěru.
- **Akce:** před spuštěním nahradit pravdivým obsahem (např. reálný počet kategorií, „nová platforma — přidej se mezi první makery"), nebo čísla úplně odstranit.

---

## 🟠 Obsahové / právní nálezy

### O1 — „Zaplatíš bezpečně přes Comgate" vs. rozhodnutí Stripe

Stránka *Jak to funguje*, krok 3: „Zaplatíš bezpečně přes Comgate kartou nebo převodem." Byznysové rozhodnutí (4. 7. 2026) je ale přejít na marketplace bránu s peněženkou (Stripe). Text sjednotit až po finálním potvrzení brány — teď je to nekonzistence mezi marketingem a plánovanou architekturou.

### O2 — Nekonzistentní oslovování (tykání × vykání)

- Homepage, *Jak to funguje*, *Pro makery*: **tykání** („Vybereš si tvůrce", „Zaplatíš online", „Převezmeš zásilku").
- Login, registrace: **vykání** („Přihlaste se a pokračujte…", „Vyberte si typ účtu…").
- Projektové pravidlo (CLAUDE.md, zatím nepotvrzené): vykání zákazníkům, tykání makerům. Rozhodnout a sjednotit — dnes se obě formy míchají v rámci jedné uživatelské cesty (homepage → registrace).

### O3 — VOP a GDPR: interim text bez konkrét, launch‑blocker trvá

- Kolega nahradil placeholder bannery reálným textem. Text **správně popisuje model „prodávající = maker"** ✓ (sedí na rozhodnutý model zprostředkovatele) a má rozumnou strukturu (7 sekcí + výčet předpisů).
- Ale je obecný: chybí konkréta rozhodnutá na schůzce — provize, lhůta 7 dní na reakci makera, 14denní okno reklamace, vratky přes Zásilkovnu, náklady vratky na makerovi, rozlišení „na zakázku × skladem" u odstoupení.
- GDPR stránka **nejmenuje zpracovatele** (rozhodnuto jmenovat: Stripe, Zásilkovna, Resend, ARES, Mapbox, Azure) — je tam jen obecné „platební a logističtí partneři".
- **Launch‑blocker Q‑0030 (schválený právní text) zůstává otevřený** — současný text je dobrý základ pro právníka, ne finál. Varovný „placeholder" banner byl odstraněn, což může budit dojem hotového textu.

### O4 — Chybí kontakt / identifikace provozovatele

VOP §1 i GDPR §1 odkazují na „kontaktní sekci" („Aktuální identifikační údaje provozovatele najdete vždy v kontaktní sekci") — **ta ale na webu neexistuje**. Povinné identifikační údaje (JVM YORE s.r.o., IČO, sídlo, e‑mail) nejsou nikde. Přidat /kontakt nebo footer blok; je to zákonná povinnost (informační povinnosti poskytovatele služby informační společnosti).

---

## 🟡 Vizuální / technické nálezy

### V1 — Mobilní menu má průhledné pozadí

Na 375 px se po rozkliknutí hamburgeru rozbalí menu (Domů / Katalog / Jak to funguje / Pro makery / Přihlášení / Začít prodávat), ale panel **nemá neprůhledné pozadí** — prosvítá pod ním hero text stránky a položky se s ním vizuálně perou. Opravit background dropdownu v `frontend/src/components/shared/public-navbar.tsx`.

### V2 — 3D hero scéna: výkonnostní varování

Konzole na homepage hlásí opakovaně `[.WebGL] GPU stall due to ReadPixels (High)` a deprecation `THREE.Clock → THREE.Timer`. Nejsou to chyby, ale na slabších zařízeních hrozí trhání/zahřívání. Zvážit: statický obrázek jako fallback na mobilu, `frameloop="demand"`, odstranění ReadPixels cesty. (Soubory: `frontend/src/components/shared/hero-scene.tsx`, `hero-scene-wrapper.tsx`.)

---

## ✅ Co je v pořádku (ověřeno)

| Oblast | Výsledek |
|---|---|
| Responzivita 375 / 768 / 1280 | ✓ layout drží, hero i sekce se správně skládají |
| Konzole (JS chyby) | ✓ žádné chyby (jen perf varování z 3D scény, viz V2) |
| Síťové requesty | ✓ žádné 4xx/5xx z prohlížeče (selhání je až SSR → API, viz K1) |
| Footer odkazy | ✓ `/katalog`, `/jak-to-funguje`, `/pro-makery`, `/register?type=maker`, `/vop`, `/gdpr` — vše správně |
| CTA „Začít prodávat" | ✓ vede na `/register?type=maker` (oprava z commitu 4e96c64 funguje) |
| Chybové stavy | ✓ katalog i login degradují srozumitelně česky, s možností opakovat |
| Registrace | ✓ přepínač Zákazník/Maker, validační hint „Alespoň 10 znaků" u hesla |
| Escrow messaging | ✓ „Platba je chráněná: maker dostane peníze až po doručení" — sedí na fungování platformy |
| Kalkulace na *Pro makery* | ✓ matematicky správně (viz K2 pozn.) |
| Meta/OG | ✓ OpenGraph + Twitter obrázky a alt texty nasazené (commit 76ab6e3) |

---

## Doporučené akce (podle priority)

1. **K1** — oživit backend na dev (ops; bez toho nejde nic dál testovat).
2. **K3** — stáhnout smyšlené statistiky (rychlá úprava, právní riziko).
3. **O4** — přidat kontakt/identifikaci provozovatele (zákonná povinnost, malá práce).
4. **K2** — rozhodnout provizi a srovnat web ↔ backend (+ ticket na věrnostní sazbu).
5. **V1** — opravit pozadí mobilního menu (drobnost, ale viditelná každému na mobilu).
6. **O2** — rozhodnout a sjednotit tykání/vykání.
7. **O1** — Comgate → Stripe v textech (až po potvrzení brány).
8. **O3** — předat současný VOP/GDPR text právníkovi jako základ; do finálu vrátit viditelné označení „pracovní verze".
9. **V2** — perf 3D scény (nice‑to‑have, sledovat na reálných mobilech).

> Návaznost: dopady bodů K2, O1 a O3 na platformu jsou rozepsané v [dopady-rozhodnuti-na-platformu.md](./dopady-rozhodnuti-na-platformu.md) (§2.1, §2.2, §2.8).

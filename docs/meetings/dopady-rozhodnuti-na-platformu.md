# Dopady byznysových rozhodnutí na platformu + výsledky revize webu

**Datum:** 4. 7. 2026
**Zdroje:** vyplněný list otázek Q1–Q17 (schůzka JVM YORE), revize nasazeného dev webu `makables-dev-web.azurewebsites.net` (4. 7. 2026), stav kódu na `origin/master` (76ab6e3).
**Účel:** podklad ke zpracování — co se v platformě musí změnit / postavit / opravit, aby odpovídala rozhodnutému modelu a šlo spustit. Navazuje na [schuzka-pravidla-a-smlouvy.md](./schuzka-pravidla-a-smlouvy.md) a [otevrene-otazky-list.md](./otevrene-otazky-list.md).

---

## 1. Souhrn rozhodnutí (z vyplněného listu)

| Q | Rozhodnutí |
|---|---|
| Q1 | **Zprostředkovatel + marketplace brána s peněženkou („B‑tok 3")** — prodávajícím vůči zákazníkovi je maker; peníze drží licencovaná platební brána v peněžence makera a uvolní je až po doručení na pokyn platformy; provizi strhává brána. |
| Q2 | **B2** — fungování platformy se zásadně nemění, právně to orámuje VOP („jménem a na účet makera"). Pozn.: B‑tok 3 přesto mění platební vrstvu (viz §2.1). |
| Q3 | JVM YORE s.r.o. **není plátce DPH**. |
| Q4 | Makeři budou plátci i neplátci. Registrace: maker vyplní údaje ručně, IČO → ARES **předvyplní** (vč. DIČ u plátců), maker **potvrdí správnost**. Detaily režimu DPH → daňový poradce. |
| Q5 | **ANO** — rozlišit produkt „na zakázku" (bez 14denního odstoupení) vs. „skladem" (s odstoupením). Nový příznak na produktu. |
| Q6 | První kontakt reklamace: zákazník píše **makerovi** (vlákno u objednávky); JVM YORE až při eskalaci. |
| Q7 | Lhůta makera na reakci na reklamaci: **7 dní**. |
| Q8 | Spor/reklamaci přes platformu lze otevřít **do 14 dní od doručení**. Zahájení přes chatové vlákno maker × zákazník; poté zákazník odešle zboží zpět makerovi **přes Zásilkovnu**. |
| Q9 | Náklady na vrácení zboží při oprávněné reklamaci nese **maker**. |
| Q10 | Refund po výplatě makerovi: **ručně** (admin, strhnutí z příští výplaty mimo systém). Nestavět zatím. |
| Q11 | Provize: **7 % základní → 3,5 % věrnostní** (po delší spolupráci), z ceny produktu, strhává se z výplaty. **NE 15 %.** |
| Q12 | Výplaty **týdně, bez minima**. |
| Q13 | SLA makera: **přijmout do 2 dnů, odeslat do 24 h** *(⚠ nejasné — viz §5.1)*. |
| Q14 | Sankce: **třístupňově** — varování → dočasné pozastavení → deaktivace. |
| Q15 | Text VOP + GDPR: **interně + externí právník** (termín nedodán). |
| Q16 | **ANO cookie lišta** (analytika/marketing budou). Nástroje zatím neurčeny. |
| Q17 | Zpracovatelé: **Stripe**, Zásilkovna, Resend, ARES, Mapbox, Azure — sedí; později doplnit (např. Cloudflare). |
| F | Vnímané chybějící funkce: rozhraní pro makery, košík, reklamační systém, chat zákazník × maker, kalkulačka na zakázkové zboží, generování a odesílání faktur všem stranám, newsletter → **rekonciliace se skutečným stavem v §3**. |

---

## 2. Dopady na platformu (od největšího)

### 2.1 Platební architektura — přechod na marketplace bránu (B‑tok 3) — **XL, největší zásah**

**Rozhodnutí:** peníze zákazníka nejdou na účet JVM YORE; drží je brána v peněžence makera, uvolnění po doručení na pokyn platformy, provizi strhne brána. V Q17 už figuruje **Stripe** → prakticky **Stripe Connect** (destination/separate charges + manual payout release), případně Mangopay/Adyen for Platforms, pokud Stripe nevyhoví.

**Co dnes je (a co to mění):**
- Dnes: Comgate, jeden merchant účet platformy ([ADR 0016](../adr/0016-payments-comgate.md)); escrow = peníze leží na účtu JVM YORE; týdenní payout batch = CSV pro hromadný bankovní převod (T‑0101–T‑0104); refund = admin → Comgate `/v1.0/refund` (T‑0105).
- Architektura je na výměnu připravená: `IPaymentProvider` je keyed adapter (`Infra.Clients/<Provider>/`), výběr přes `CountryConfiguration.DefaultPaymentProvider`. Nový provider = nová implementace, handlery se nemění.

**Co je potřeba postavit/změnit:**
1. **Nový `IPaymentProvider` adaptér „stripe"** (Checkout Session / PaymentIntent, webhooky `payment_intent.succeeded` apod., idempotence dle vzoru ADR 0016 — signature verification místo IP allowlistu).
2. **KYC onboarding makerů** — Stripe Connect Express: nový krok v registraci/aktivaci makera (onboarding link, stav účtu, webhook `account.updated`). Maker bez dokončeného KYC nesmí publikovat produkty / přijímat objednávky. **Nová entita/stav na Makerovi** (`PayoutAccountRef`, `PayoutAccountStatus`).
3. **Uvolňování peněz po doručení** — místo „týdenní CSV batch z našeho účtu" bude payout = **pokyn bráně k uvolnění/transferu** (per objednávka po `Delivered`, nebo týdenní agregace transferů — zachovat týdenní rytmus dle Q12). Přestavba `PayoutBatch` logiky: batch přestává být bankovní CSV, stává se dávkou transfer-pokynů + odsouhlasení stavu přes webhooky.
4. **Provize** — strhává brána při splitu (application fee). Výpočet zůstává v `OrderPricing` (snapshot na objednávce se nemění), jen realizace peněz se přesouvá do brány.
5. **Refundy** — přes Stripe API; před výplatou jednoduché (peníze ještě v peněžence); po výplatě zůstává ruční proces (Q10).
6. **Comgate zůstane v kódu** jako neaktivní adaptér (keyed služby to snesou); přepnutí = `CountryConfiguration.DefaultPaymentProvider = 'stripe'` + seed.
7. **Nový ADR** (nahrazuje/doplňuje 0016) — zdokumentovat zvolený tok: kdo je merchant of record, kdy se uvolňuje, co se děje při sporu (hold prodloužit), co při zrušení KYC.

**Ověřit před stavbou (blokuje návrh):** poplatky Stripe (CZ karty ~1,5 % + 6,5 Kč + Connect fee) vs. Comgate; podpora „hold do doručení" (manual payouts / `transfer_data` s odloženým transferem); limity pro neověřené účty; zda čeští malí IČaři projdou Express KYC bez problémů.

### 2.2 Provize 7 % / 3,5 % věrnostní — **M** (+ okamžitá oprava nesouladu)

- **Nesoulad k opravě hned:** web už komunikuje 7 % → 3,5 % (stránka *Pro makery*, homepage hero „7 % > 3,5 %"), ale backend seed má **15 %** — [CountrySeed.cs:40](../../backend/src/Makables.Infra.Database/Seeding/CountrySeed.cs) (`platformFeeRateBp: 1500`). Do vyjasnění buď stáhnout čísla z webu, nebo (rozhodnuto-li 7 %) migrace seedu na `700`.
- **Věrnostní sazba je nová funkce:** dnes je sazba jedna na zemi (`CountryConfiguration.PlatformFeeRateBp`). Per‑maker sleva neexistuje. Potřeba: `Maker.FeeRateOverrideBp` (nullable; pricing bere `maker.override ?? country.default`), admin akce na nastavení (auditovaná), definice pravidla „po delší spolupráci" (viz §5.2 — kritéria nejsou určena), promítnutí do fee faktury.
- `OrderPricing` čte sazbu už dnes z configu, snapshot na objednávce zůstává — změna je bezpečná pro historické objednávky.

### 2.3 Fakturace v modelu zprostředkovatele + DPH neplátce — **L**

Dnes: JVM YORE vystavuje zákaznickou fakturu svým jménem (QuestPDF, ADR 0025) a fee fakturu makerovi při payout batchi. V modelu „prodávající = maker" (Q1+Q2‑B2):

1. **Zákaznická faktura musí být vystavena jménem a na účet makera** (issuer = maker: název, IČO, adresa, DIČ je-li plátce; doložka „vystaveno zprostředkovatelem JVM YORE s.r.o. jménem a na účet prodávajícího"). Šablona + data: dnes šablona bere identitu platformy — potřeba nový vzor + doplnit fakturační identitu makera (z ARES dat už ji máme).
2. **DPH na zákaznické faktuře se řídí makerem, ne zemí:** maker plátce → faktura s DPH (jeho DIČ); maker neplátce → bez DPH („není plátcem DPH"). Dnes DPH řídí `CountryConfiguration.InvoicingMode` (per země) — **nutná per‑maker větev** (`Maker.IsVatPayer` odvozené z DIČ). Číslování faktur: dnes centrální řada platformy — právně ověřit, zda řada „vystavovaná jménem makerů" může zůstat jedna (viz §5.3, otázka pro daňového poradce).
3. **Fee faktura (provize) JVM YORE → maker: bez DPH** (JVM YORE neplátce, Q3) — dnes už `InvoicingMode.None` podporujeme, jen pohlídat, že fee faktura nese správnou doložku. ⚠ **Hlídat obrat 2 mil. Kč** — provize je obrat JVM YORE; po překročení povinná registrace k DPH a fee faktury se mění (viz §5.3).
4. Q4 (registrace: ruční údaje + ARES předvyplnění + potvrzení) — ARES integrace existuje (T‑0032); ověřit, že UX odpovídá: předvyplnit, editovatelné, checkbox potvrzení správnosti.

### 2.4 Produktový příznak „na zakázku" × „skladem" — **M**

Nový sloupec na produktu (`FulfillmentType: MadeToOrder | InStock` — default `MadeToOrder`), maker volí při zakládání; badge na detailu produktu; **text u checkoutu**: u „na zakázku" povinné poučení o výjimce z 14denního odstoupení (§ 1837 písm. d) OZ — spotřebitel musí být poučen před objednávkou), u „skladem" naopak informace o právu odstoupit. Promítnout do VOP. Backend + FE + NSwag regen + i18n.

### 2.5 Reklamační proces (Q6–Q9) — **L**

Mapování na existující dispute systém ([dispute.md](../architecture/roles/dispute.md)):

| Pravidlo | Dnes | Změna |
|---|---|---|
| Otevření do 14 dní od doručení (Q8) | dispute lze otevřít v `Delivered` bez časového limitu | přidat okno `DeliveredAt + 14 dní` pro zákaznické otevření (admin bez limitu — zákonná práva běží dál, jen mimo platformní tlačítko) |
| První kontakt maker (Q6) | vlákno zpráv u objednávky existuje (T‑0079) | UX: „Reklamovat" vede nejdřív do vlákna s předvyplněnou kategorií; teprve „eskalovat na Makables" otevře dispute pro admin |
| Maker reaguje do 7 dní (Q7) | žádný timer | nový timer: neodpoví‑li maker do 7 dnů, auto‑eskalace na admin (Function, vzor auto‑deliver) + e‑mail |
| Vratka přes Zásilkovnu zpět makerovi (Q8) | neexistuje reverse logistika | **nová integrace**: vygenerovat zpětný štítek Packeta (zákazník → maker); náklady jdou za makerem (Q9) — účtovat proti výplatě nebo fee faktuře |
| Náklady vratky maker (Q9) | — | byznys pravidlo do smlouvy s makery + promítnout do payout/fee logiky |

Pozn.: pozdější reklamace (do 24 měsíců ze zákona) běží mimo platformní tlačítko — e‑mail/kontakt, řeší maker; VOP to musí říct.

### 2.6 SLA makera (Q13) — **M** (⚠ vyjasnit, viz §5.1)

Dnes není žádná lhůta na přijetí/odeslání (jen autocancel nezaplacených po 24 h). Návrh po vyjasnění: timer „nepřijato do 2 dnů" → upozornění makerovi → eskalace/auto‑storno s refundem; lhůta odeslání per rozhodnutí (24 h od čeho? — viz §5.1). Nové Functions + e‑maily + stavy.

### 2.7 Třístupňové sankce (Q14) — **M**

Dnes: jen `IsActive` (tvrdá deaktivace). Potřeba: evidence prohřešků (pozdní odeslání, prohrané reklamace…), stavy `Warning` / `Suspended(od–do)` / `Deactivated`, admin akce (auditované — vzor `IAdminAuditableCommand` existuje), dopad suspendace (skrytí produktů, blokace nových objednávek, rozpracované dokončit). Pravidla eskalace (kolik varování → pozastavení) definovat ve smlouvě s makery.

### 2.8 Cookie lišta + GDPR + Kontakt — **S/M** (frontend, před launchem)

1. **Cookie lišta** (Q16): consent management — nezbytné vs. analytické/marketingové, blokace skriptů před souhlasem, uložení volby; poté teprve nasadit analytiku. GDPR stránka §6 už „nastavení souhlasu" slibuje — teď neexistuje.
2. **GDPR stránka**: vyjmenovat zpracovatele (Q17: Stripe, Zásilkovna, Resend, ARES, Mapbox, Azure) — kolegou dodaný text je obecný („platební a logističtí partneři"), doplnit jmenovitě.
3. **Kontaktní stránka/sekce chybí** — VOP §1 i GDPR §1 na ni odkazují („identifikační údaje najdete v kontaktní sekci"), ale neexistuje; povinné údaje provozovatele (JVM YORE s.r.o., IČO, sídlo, e‑mail) nejsou nikde na webu. Přidat (footer blok nebo /kontakt).
4. VOP/GDPR text na webu je interim — finální text interně + právník (Q15); launch‑blocker Q‑0030 v [launch‑checklist](../launch-checklist.md) **zůstává otevřený**.

---

## 3. Rekonciliace bloku F („co chybí") se skutečným stavem

| Požadavek (blok F) | Stav v platformě | Poznámka |
|---|---|---|
| Rozhraní pro makery | ✅ **existuje** | maker dashboard: objednávky (přijetí, expedice, štítky), produkty vč. obrázků, výplaty, odpovědi na recenze. Pokud chybí něco konkrétního — vypsat co. |
| Chat zákazník × maker | ✅ **existuje** (T‑0079) | vlákno zpráv **u objednávky** (+ unread badge, e‑mail digest). Neexistuje chat **před nákupem** (trust‑model rozhodnutí z Batch 1 — bez pre‑purchase kontaktu). Pokud je požadavek předprodejní chat → nové rozhodnutí + funkce. |
| Generování a odesílání faktur | ✅ částečně | zákaznická faktura (PDF, po zaplacení) + fee faktura makerovi (při payoutu) se generují automaticky a jsou ke stažení. **Mění se** obsah dle §2.3 (jménem makera, DPH per maker). „Odesílání všem stranám" e‑mailem jako příloha — dnes jen odkaz/stažení, doplnit lze. |
| Reklamační systém | 🟡 částečně | dispute systém + admin rozhodování existuje; chybí zákaznické UX „reklamace" (Q6–Q8 tok), 7denní timer, 14denní okno a zpětná doprava Zásilkovnou (§2.5). |
| Košík | ❌ **neexistuje** | checkout je „1 produkt → objednávka" přímo. Vícepoložkový košík = větší zásah (Order má 1 produkt; multi‑item = multi‑maker split objednávek). **Rozhodnout: MVP, nebo v1.1?** (doporučení: v1.1 — u zakázkové výroby je single‑item obhajitelné) |
| Kalkulačka na zakázkové zboží | ❌ neexistuje | souvisí s odloženou otázkou Q‑0002 (on‑request produkty / poptávka s nacením). **Rozhodnout scope**: jednoduchá poptávka „pošli brief → maker nacení" vs. parametrická kalkulačka (materiál/rozměr/množství). Druhé je výrazně větší. |
| Newsletter | ❌ neexistuje | vyžaduje souhlas (navazuje na cookie/consent §2.8); e‑mail infrastruktura (Resend) existuje. Rozhodnout: MVP, nebo v1.1? |

---

## 4. Výsledky revize dev webu (4. 7. 2026, `makables-dev-web.azurewebsites.net`)

### 🔴 Kriticky

1. **Backend na dev prostředí neběží.** Všechny API hosty (`makables-dev-public/customer/maker.azurewebsites.net`) vrací 503/timeout (DNS OK → App Service existuje, aplikace zastavená nebo padá při startu). Důsledek: katalog hlásí „Server je momentálně nedostupný", přihlášení/registrace nefunkční — web je jen fasáda, nejde проверit E2E. **Akce: Azure portál → Log stream / stav App Service; ověřit, zda backend deploy vůbec proběhl.** (Frontend chybové stavy fungují vzorově — česky, s „Zkusit znovu".)
2. **Provize na webu (7 %/3,5 %) ≠ backend (15 %)** — detail v §2.2.
3. **Smyšlené statistiky:** „250+ ověřených makerů", „4,9/5 průměrné hodnocení" (homepage, login, registrace). Při launchi s nulou makerů = klamavá obchodní praktika (riziko ČOI). Stáhnout / nahradit pravdivým obsahem před spuštěním.

### 🟠 Obsahové

4. **„Zaplatíš bezpečně přes Comgate"** (stránka *Jak to funguje*) — v rozporu s rozhodnutím Stripe (§2.1). Sjednotit po finálním potvrzení brány.
5. **Nekonzistentní oslovování:** homepage / *Jak to funguje* / *Pro makery* tykají i zákazníkům; login/registrace vykají. Projektové pravidlo: vykání zákazníkům, tykání makerům. Sjednotit (rozhodnout, zda pravidlo platí — pak je homepage k přepsání do vykání).
6. **VOP/GDPR:** text správně popisuje model „prodávající = maker" ✅, ale je obecný/interim — chybí konkréta rozhodnutá na schůzce (provize, lhůty 7/14 dní, vratky přes Zásilkovnu, náklady makera). Předat právníkovi spolu s tímto dokumentem.
7. **Chybí kontakt/identifikace provozovatele** — §2.8 bod 3.

### 🟡 Vizuální / technické

8. **Mobilní menu má průhledné pozadí** — pod rozbaleným menu prosvítá obsah stránky (hero text). Doplnit neprůhledný background dropdown panelu (`public-navbar.tsx`).
9. **3D hero scéna**: konzole hlásí WebGL perf varování („GPU stall due to ReadPixels", opakovaně) + deprecated `THREE.Clock` → `THREE.Timer`. Na slabších zařízeních riziko trhání; zvážit statický fallback na mobilu / `frameloop='demand'`.

### ✅ Co je v pořádku

Responzivita 375/768/1280 ✓ · footer odkazy správně (`/vop`, `/gdpr`, `/register?type=maker`) ✓ · konzole bez JS chyb ✓ · error stavy graceful ✓ · příklad kalkulace výplaty na *Pro makery* matematicky správně (1 009 / 1 044 Kč, sedí na vzorec platformy) ✓ · escrow formulace „maker dostane peníze až po doručení" sedí ✓ · heslo „alespoň 10 znaků" ✓.

---

## 5. Otevřené body k vyjasnění (než se začne stavět)

1. **Q13 „odeslat do 24 h"** — 24 h od čeho? Od přijetí objednávky je to u zakázkové výroby (3D tisk trvá dny) nereálné. Pravděpodobný záměr: „odeslat do 24 h **od dokončení výroby**", nebo per‑produkt lhůta zadaná makerem (např. „dodání do X dní" na produktu). **Potvrdit výklad.**
2. **Kritéria věrnostní provize 3,5 %** — „po delší spolupráci" = kolik měsíců / objednávek / obratu? Přiznává se automaticky, nebo admin ručně? (Pro MVP stačí admin ručně přes fee‑override, §2.2.)
3. **Daňový poradce (navazuje na Q3/Q4):** (a) může platforma vystavovat faktury jménem makerů v jedné číselné řadě, nebo per‑maker řada? (b) režim neplátce‑maker vs. plátce‑maker na zákaznické faktuře; (c) hlídání obratu JVM YORE 2 mil. Kč (provize) → povinná registrace k DPH.
4. **Potvrzení brány:** Stripe Connect (Express) — ověřit poplatky, hold‑do‑doručení, KYC průchodnost pro malé IČaře; fallback Mangopay/Adyen. Právník: potvrzení, že B‑tok 3 nevyžaduje registraci JVM YORE u ČNB.
5. **Scope MVP vs. v1.1:** košík, kalkulačka/poptávka na míru, newsletter, předprodejní chat (§3).
6. **Termín finálního VOP/GDPR textu** (Q15 bez data) — launch‑blocker Q‑0030.

---

## 6. Navržené pořadí prací

| # | Balík | Velikost | Blokuje launch? |
|---|---|---|---|
| 1 | Oživit backend na dev (deploy/ops) | S | ✅ (nic nejde testovat) |
| 2 | Rychlé opravy webu: statistiky pryč, kontakt/identifikace, mobilní menu, sjednotit tón | S | ✅ (právní riziko + povinné údaje) |
| 3 | Rozhodnout provizi definitivně → seed 700 bp + web; fee‑override per maker (admin ručně) | M | ✅ (čísla vůči makerům) |
| 4 | Stripe Connect spike: ověřit hold/split/KYC + poplatky → nový ADR | M | ✅ (rozhoduje architekturu plateb) |
| 5 | Stripe adaptér + maker KYC onboarding + release‑po‑doručení + refundy | XL | ✅ |
| 6 | Fakturace jménem makera + DPH per maker | L | ✅ |
| 7 | Příznak „na zakázku/skladem" + poučení u checkoutu | M | ✅ (spotřebitelské právo) |
| 8 | Reklamační tok (14 dní okno, 7denní timer, zpětný štítek Zásilkovna) | L | 🟡 (min. varianta: okno + eskalace ručně) |
| 9 | Cookie lišta + GDPR zpracovatelé | M | ✅ |
| 10 | SLA timery makera (po vyjasnění §5.1) + sankční stavy | M | 🟡 |
| 11 | Košík / kalkulačka / newsletter | L–XL | ❌ (v1.1 kandidáti) |

> Pozn. pro zpracování: každý balík z tabulky by měl vzniknout jako ticket v `docs/tickets/` podle šablony projektu (INDEX + acceptance criteria). Body 5 a 6 vyžadují nejdřív ADR (architect), protože mění peníze a fakturaci — viz pravidla v CLAUDE.md.

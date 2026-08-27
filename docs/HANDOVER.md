# Makables — technický handover

**Datum:** 2026-08-27
**Stav repozitáře:** `master` @ `8cf9992`, pracovní strom čistý
**Nahrazuje:** [HANDOFF.md](./HANDOFF.md) — ten je discovery-fáze handoff z 2026-05-21 (před napsáním kódu) a je dnes historický dokument, ne stav projektu.

Tento dokument je jediný vstupní bod pro někoho, kdo projekt přebírá. Popisuje **co Makables je**, **jak je postavené**, **co je hotové**, **co hotové není** a **co je potřeba opravit**. Všechna čísla níže jsou naměřená v této session, ne odhadnutá — zdroj je uveden u každého.

---

## 1. Co je Makables

Český marketplace, kde zákazník objedná zakázkovou výrobu od ověřeného výrobce („maker“): 3D tisk, klasický tisk, potisk textilu, laser/CNC, velkoformát, handmade.

| | |
|---|---|
| Značka | Makables — „Where Ideas Take Shape.“ |
| Doména | makables.cz (**před spuštěním**) |
| Provozovatel | JVM YORE s.r.o. |
| Trh při spuštění | pouze CZ, architektura připravená na multi-country |
| Cloud | Azure, West Europe |

**Obchodní tok:** maker se registruje přes IČO (ARES prefill) → admin ho ověří → maker vystaví výrobky → zákazník objedná a zaplatí (Comgate) → maker přijme, vyrobí a odešle (Zásilkovna nebo osobní odběr) → doručeno → týdenní výplatní dávka vyplatí makera po odečtení provize. Platforma vystavuje fakturu zákazníkovi a poplatkovou fakturu makerovi.

**Doménový model (agregáty):** `Order`, `Maker`, `Product`, `User`, `Invoice`, `PayoutBatch`, `Dispute`, `Category`, `Review`, `OrderMessage`, `Address`, `CountryConfiguration`.

**Stavový automat objednávky** ([OrderState.cs](../backend/src/Makables.Core.Domain/Orders/OrderState.cs)) — 9 stavů:
`PendingPayment → Paid → Accepted → Shipped → Delivered → Completed`, plus `Cancelled` / `Refunded` / `Disputed` jako odbočky.

---

## 2. Technologie a kde co leží

### 2.1 Stack

| Vrstva | Technologie |
|---|---|
| Backend | .NET 10 (SDK 10.0.202), ASP.NET Core, MediatR, FluentValidation, EF Core 10 |
| Databáze | PostgreSQL 16 |
| Backend hosty | čtyři podle publika: `Web.Customer` (5001), `Web.Maker` (5002), `Web.Admin` (5003), `Web.Public` (5104) |
| Background jobs | Azure Functions v4 (isolated worker, Docker) |
| Úložiště souborů | Azure Blob Storage — **výhradně přes backend**, prohlížeč nikdy nesahá na blob URL |
| Auth | vlastní: Argon2id + JWT (HS256, audience per host) + refresh tokeny, HttpOnly cookies |
| Frontend | Next.js 16.2.6 (App Router), React 19.2.4, Tailwind 4 |
| Kontrakt | OpenAPI → NSwag → TypeScript klient v `frontend/src/lib/api-client/` |
| PDF | QuestPDF (faktury, poplatkové faktury) |
| 3D hero | three.js + @react-three/fiber |
| Grafy | chart.js (admin revenue) |
| Testy | xUnit (`Makables.Tests`, `Makables.IntegrationTests`), Vitest + Testing Library + jest-axe |
| IaC | Bicep (`infra/bicep/`) |
| CI/CD | GitHub Actions |

### 2.2 Rozložení repozitáře

```
makables/
├── backend/src/            # Makables.Api.slnx — 17 projektů, 944 .cs souborů
│   ├── Makables.Core.Domain/          # agregáty, VO, rozhraní repozitářů — ŽÁDNÉ third-party balíčky
│   ├── Makables.Core.AppServices/     # MediatR use-casy, validátory, DTO — 119 souborů, ŽÁDNÉ EF Core
│   ├── Makables.Config/               # sdílené host wiring: auth, DI, middleware, observabilita, sdílené controllery
│   ├── Makables.Infra.Common/
│   ├── Makables.Infra.Database/       # EF Core, 53 migrací
│   ├── Makables.Infra.Clients/        # Ares, Comgate, Packeta, Mapbox, Resend, SendGrid, Google, Apple, Dev
│   ├── Makables.Infra.Azure.Storage.Blobs/
│   ├── Makables.Infra.PdfRendering/   # QuestPDF
│   ├── Makables.Web.{Customer,Maker,Admin,Public}/   # tenké hosty
│   ├── Makables.Functions/            # 11 Functions (outbox, e-maily, timery, payouty)
│   ├── Makables.Tools.Seeder/         # realistický CZ dev dataset
│   └── Makables.{Tests,IntegrationTests,TestUtilities}/
├── frontend/src/           # 358 .ts/.tsx souborů
│   ├── app/(public) (auth) (customer) (maker) (admin)   # 43 stránek
│   ├── components/ui/      # 22 primitiv (button, dialog, dropdown, date-picker, …)
│   ├── components/shared/
│   └── lib/                # api-client (generovaný), api-client-helpers, runtime, i18n, auth, theme, …
├── docs/                   # systém záznamu projektu — 28 ADR, 34 role files, 161 ticketů, runbooky
├── agents/                 # operační systém agentů: process, knowledge, templates
├── .claude/                # charty agentů + slash commands
├── infra/bicep/            # main.bicep + 10 modulů
├── deploy/load-tests/      # k6
└── .github/workflows/      # ci, deploy-staging, deploy-production, ops-diagnostics
```

### 2.3 Architektura — pravidla, která platí

**Vrstvení (Clean Architecture, závislosti míří dovnitř):**
`Core.Domain` ← `Core.AppServices` ← `Web.*` / `Functions`; `Infra.*` implementuje rozhraní z `Core.Domain`. Web hosty **nikdy** nereferencují `Infra.*` přímo.

**DDD:** agregáty mají privátní settery, vznikají přes `static Create(...)`, mění se přes intent-metody (`MarkAsPaid`, `Ship`, `Cancel`, `RevertAcceptance`). Invarianty jsou v agregátu, ne v handleru. Jeden agregát na transakci — cross-aggregate efekty jdou přes **Outbox**.

**CQRS:** jeden soubor na use-case v `Core.AppServices/Features/<Agregát>/<UseCase>.cs` s vnořenými `Command`/`Query`, `Response`, `Validator`, `Handler`. Pipeline behaviors (`ValidationPipelineBehavior`, `UnitOfWorkPipelineBehavior`, `AdminAuditPipelineBehavior`) běží automaticky — handlery **nevolají** `SaveChangesAsync()`.

**Peníze:** `Money` = `long` minorové jednotky + `string Currency`. Nikdy `decimal`, nikdy `double`. Každý peněžní sloupec je `*_minor BIGINT NOT NULL` + `currency CHAR(3)`. DPH v basis pointech.

**Bezpečnost dat:** vlastnictví vynucují **scoped repozitáře** (`ForCustomer` / `ForMaker` / `Unscoped`), ne `if` v handleru. Cross-tenant čtení vrací prázdno, ne 403 (ADR 0013). Soft delete přes `Auditable` + globální query filter.

**Per-country variace:** čte se z řádku `CountryConfiguration` (VAT, provize, poskytovatelé, timezone, ZIP formát, cena dopravy). Nikdy `if (countryCode == "CZ")` mimo per-country adaptér.

**Outbox:** 20 typů událostí ([OutboxEventTypes.cs](../backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs)) — e-maily, generování faktur, generování štítků. `ProcessOutboxFunction` (timer 30 s) → fronta → `SendEmailFunction`. Retry 1m→5m→15m→1h→6h→24h, po 6 pokusech `stalled` a admin to vidí v konzoli.

**Kontrakt:** `frontend/src/lib/api-client/` je **generovaný** — pre-commit hook blokuje ruční editaci. Každá změna kontraktu regeneruje klienta ve stejném PR; CI ověřuje paritu hashů (`npm run check:api`).

### 2.4 Externí integrace

| Doména | Poskytovatel | Kde |
|---|---|---|
| Platby | Comgate (webhook s IP allow-listem, žádné HMAC) | `Infra.Clients/Comgate/` |
| Doprava | Zásilkovna / Packeta (+ zpětný štítek) | `Infra.Clients/Packeta/` |
| Firemní rejstřík | ARES (mod-11 validace IČO, 24h cache + 7denní stale fallback) | `Infra.Clients/Ares/` |
| E-mail | **Resend** (ADR 0019 re-amendován v T-0157; SendGrid adaptér zůstal v repu) | `Infra.Clients/Resend/` |
| Geokódování | Mapbox (backend proxy, per-user rate limit) | `Infra.Clients/Mapbox/` |
| OAuth | Google + Apple (Apple podepisuje ES256 client secret za běhu) | `Infra.Clients/{Google,Apple}/` |
| Dev bypass plateb | env-gated `DevPaymentsController` | `Infra.Clients/Dev/` |

### 2.5 Background jobs (11 Functions)

`ProcessOutboxFunction`, `SendEmailFunction`, `GenerateInvoiceFunction`, `GenerateLabelFunction`, `AutoDeliverOrdersFunction`, `SyncShipmentStatusesFunction`, `CancelExpiredPendingPaymentOrdersFunction`, `RunWeeklyPayoutBatchFunction`, `DisputeAutoEscalationFunction`, `EvictExpiredRegistryCacheFunction`, `DataRetentionCleanupFunction`.

---

## 3. Stav — co je hotové

Backlog je v [docs/tickets/INDEX.md](./tickets/INDEX.md) (167 řádků, 161 rozepsaných ticketů) rozdělený do 8 fází. **Podle INDEXu je 150 ticketů `done`.**

| Fáze | Rozsah | Stav |
|---|---|---|
| 1 — Foundation scaffold | T-0001–T-0016 | ✅ hotová |
| 2 — Identity (auth, users, makers) | T-0020–T-0036, T-0139 | ✅ hotová |
| 3 — Katalog (produkty, browse) | T-0040–T-0050 | ✅ hotová |
| 4 — Objednávky | T-0060–T-0089 | ✅ hotová |
| 5 — Post-order (recenze, výplaty, refundy, spory, admin) | T-0100–T-0118c | ✅ hotová |
| 6 — Polish (statické stránky, SEO, k6, a11y, runbooky) | T-0130–T-0138 | ✅ kód hotový, manuální RUN kroky viditelné v [launch-checklist.md](./launch-checklist.md) |
| 7 — Business-model pivot | T-0140–T-0151 | ⚠️ částečně — 5 hotových, 6 `draft` blokovaných obchodním rozhodnutím, 1 rozdělený |
| 8 — Site-wide UX sweep | T-0166–T-0181 | ✅ 13/13 `ready` ticketů merged (PR #138–#152) |
| — Post-sweep | T-0186–T-0196 | ✅ merged (PR #153–#166) |

**Konkrétně funguje (kód na `master`):**

- **Auth:** registrace, přihlášení, odhlášení, refresh, magic link, potvrzení e-mailu, reset hesla, Google OAuth, Apple Sign-In, per-host audience enforcement, lockout, rate limit, cookie→JWT most, session refresh v middleware.
- **Makeři:** registrace přes IČO s ARES prefill + validací, admin ověření / deaktivace / refresh z ARES, profil, bankovní účet (ČNB mod-11), osobní odběr, kategorie, logo.
- **Katalog:** stránkovaný seznam makerů s filtry (kategorie / město / min. hodnocení), profil makera podle slugu, detail výrobku, veřejné recenze, obrázky přes ETag-cachovaný streaming endpoint.
- **Produkty:** CRUD, upload obrázků (≤10, magic-byte sniff, ≤5 MB), typ ceny `Fixed`/`From`/`OnRequest`, typ plnění `MadeToOrder`/`InStock`.
- **Objednávky:** checkout, přílohy, Comgate platba + webhook (idempotentní), generování faktury, přijetí/odmítnutí makerem, odeslání přes Zásilkovnu se štítkem, osobní předání, doručení (manuální / auto / dle dopravce), zprávy mezi stranami, storno nezaplacené objednávky zákazníkem, odmítnutí zaplacené makerem v okně.
- **Post-order:** recenze + odpověď makera, spory s 14denním oknem a 7denním timerem odpovědi, zpětný štítek, refundy, týdenní výplatní dávky s CSV pro banku a poplatkovými fakturami.
- **Admin:** přihlášení, přehled s KPI, všechny objednávky/faktury/výplaty, detail objednávky s peněžními akcemi, kategorie, makeři, uživatelé (GDPR erase s ověřenou identitou), country config, triáž zaseknutého outboxu, audit log, panel výdělků platformy + graf tržeb v čase.
- **Veřejné:** landing s 3D hero, jak to funguje, pro makery, VOP, GDPR, kontakt, cookie banner, sitemap + robots + OG metadata, světlé i tmavé téma.

### Ověřeno v této session

| Kontrola | Výsledek |
|---|---|
| `dotnet test Makables.Tests` | **2 283 prošlo, 0 selhalo** (7 s) |
| `dotnet test Makables.IntegrationTests` | **360 prošlo, 0 selhalo** (30 s, proti lokálnímu PG clusteru) |
| `npx vitest run` (frontend) | **317 prošlo v 55 souborech, 0 selhalo** (10,6 s) |
| `npx tsc --noEmit` | čistý, exit 0 |
| `npx eslint` | 0 chyb, **12 varování** (nepoužité importy) |
| `git status` | čistý strom |

**Neověřeno:** `npm run check:api` (vyžaduje běžící všechny čtyři hosty), žádný průchod reálným prohlížečem, žádný běh proti Azure.

---

## 4. Co hotové není

### 4.1 Rozpracované / neuzavřené

| Ticket | Stav | Co chybí |
|---|---|---|
| **T-0153** | `in_progress` | **E2E průchod jádrem marketplace.** Všechny stavební kameny jsou hotové, ale smyčka (maker vystaví → katalog → zákazník objedná a zaplatí → maker odešle → doručeno) **nikdy nebyla projita jako jedna souvislá cesta**. Blokuje ji oživení dev prostředí v Azure + vyřešení cookie domény (na holém `*.azurewebsites.net` login nefunguje — public-suffix list). |
| **T-0180** | `ready`, rozpracovaná větev | Reaktivace soft-smazaných entit. Na `feat/T-0180-reactivation-paths` leží WIP commit `74ddb59` (`Auditable.Reactivate` + testy) — **není na masteru**. |
| **T-0179** | `draft` | Kontraktní follow-upy z Phase 8: badge s počty, souhrn nasčítaného zůstatku výplat, nastavení hlavního obrázku, `productId`/`makerSlug` v DTO objednávky, backendový `isActive` filtr produktů. Bez obchodního blokátoru, potřebuje jen grooming kontraktu. |
| **T-0163** | `draft` | Makerem navržené kategorie se schválením adminem. |

### 4.2 Blokované obchodním rozhodnutím (Phase 7)

| Ticket | Blokuje |
|---|---|
| **T-0142** → rozděleno na T-0182–T-0185 | Stripe Connect Express. Uživatel Stripe potvrdil 2026-08-22, ale **Q-0036** (fee/hold mechanika, KYC průchodnost, **registrační povinnost u ČNB — potřebuje psaný právní posudek**) zůstává otevřená. |
| **T-0143** | Fakturace jménem makera + per-maker DPH. Blokuje **§5.3** — otázky na daňového poradce (sdílená vs. per-maker číselná řada, formulace pro plátce/neplátce DPH, hlídání obratu 2 M Kč). |
| **T-0148** | SLA timery makera + třístupňové sankce. Blokuje **§5.1** — „odeslat do 24 h od čeho?“ U zakázkové výroby je 24 h od přijetí neproveditelné. |
| **T-0149 / T-0150 / T-0151** | Košík (multi-maker split), poptávkový kalkulátor, newsletter. Blokuje **§5.5** — rozhodnutí MVP vs. v1.1. Explicitně **ne**blokující spuštění. |

### 4.3 Blokátory před spuštěním

Plný seznam: [docs/launch-checklist.md](./launch-checklist.md). Nejtvrdší položky:

- 🔴 **Q-0030 — právní text.** `/vop` a `/gdpr` jsou naskládané jako skořápka s viditelným placeholder bannerem. Schválený text od JVM YORE chybí. Bez něj se nespouští.
- 🔴 **Azure secrets + OIDC.** Deploy secrets nejsou nastavené, resource groupy `rg-makables-weu-{dev,prod}` a federovaná credential nejsou vytvořené. Deploy fail-closed abortuje.
- 🔴 **Registrace Google + Apple sign-in v produkci.** Apple vyžaduje placený Developer Program; Google consent screen je v režimu Testing.
- 🔴 **Infra hardening:** `AzureWebJobsStorage` na managed identity, Postgres Private Endpoint (prod), Blob GRS, Blob soft-delete 30 dní.
- 🟡 Secrets do Key Vaultu (dnes jako App Settings — funkční, ale ADR 0023 §7 chce KV reference).
- 🟡 `NEXT_PUBLIC_SITE_URL`, OG obrázek, odeslání sitemap do Search Console.

### 4.4 Otevřené otázky

[docs/questions/open.md](./questions/open.md) — 41 otázek celkem, **24 otevřených**, z toho **2 blokující**: Q-0030 (právní text) a Q-0036 (Stripe/ČNB). Zbytek je technický dluh s obhájeným defaultem (PKCE u Google OAuth, timeout na Comgate HttpClient, kompozitní indexy pro alternativní řazení, rozpad i18n slovníku do 17 client chunků, chybějící bundle budget).

---

## 5. Co je potřeba opravit

Seřazeno podle toho, jak snadno to kousne.

### 5.1 Chybějící český překlad chybového kódu (reálná chyba, malá)

`BusinessErrorMessage.OrderRefusalWindowExpired` (`"order.refusalWindowExpired"`) **nemá cs-CZ klíč** — přišel s T-0181 (PR #152). Když makerovi vyprší okno pro odmítnutí zaplacené objednávky, uvidí syrový kód místo české věty. Porušuje pravidlo parity, které si projekt sám vynucuje (`ruleT8`).
Doklad: `node scripts/check-consistency.mjs` → řádek T8.
Oprava: přidat klíč do [cs-CZ.ts](../frontend/src/lib/i18n/cs-CZ.ts).

### 5.2 `check-consistency.mjs` není zapojený do CI ani do pre-commit hooku

Skript existuje, má 9 pravidel a baseline — ale **nic ho nespouští**. `.github/workflows/ci.yml` má čtyři joby (backend, frontend, api-parity, bicep) a `.husky/pre-commit` volá jen kontrolu ručních editací NSwag klienta. Důsledek: **33 nových nálezů proti baseline** se nikdy neukázalo v žádném PR — včetně 5.1 výše.

Rozpad těch 33: 24× T1 (tvar feature souboru — většinou legitimní, `EmailHtmlLayout.cs` / `EmailFormatting.cs` / `RevenueReportingCalendar.cs` nejsou use-casy a patří jinam než do `Features/`), 6× T5 (`Error.NotFound` s inline stringem místo `BusinessErrorMessage.X`), 2× T3 (`SaveChangesAsync` v `IOutboxDispatcher` — pravděpodobně vědomé, dispatcher běží mimo MediatR pipeline), 1× T8 (viz 5.1).

**Doporučení:** zapojit do CI jako samostatný job, předtím ale rozhodnout u T1/T3 nálezů, co je vědomá výjimka (`--update-baseline`) a co skutečný dluh.

### 5.3 Stav ticketů driftuje ve třech místech

Frontmatter ticketů, sloupec State v INDEXu a realita na masteru se rozcházejí:

- **12 ticketů má frontmatter `in_review`, ale jsou dávno merged** — T-0181, T-0186–T-0192, T-0194, T-0195 (PR #152–#163). INDEX u části z nich taky pořád říká `in_review`.
- **3 mají `in_progress`, ale jsou merged** — T-0049c (PR #21), T-0140 (PR #80), T-0162.
- **T-0050 má `draft`, ale je merged** (PR #89).
- **T-0193 a T-0196 nemají ticket soubor vůbec** — přitom jejich kód je na masteru (PR #160 „reveal toggle na heslech + potvrzení při registraci“, PR #165 „branded transakční e-maily“).
- **T-0182–T-0185 nemají ticket soubory ani řádky v INDEXu** — plánovací commit `768507a` je jen na větvi `feat/T-0180-reactivation-paths`, nikdy nedošel na master.

Tohle už jednou nastalo (commit `16fe54e` „sync INDEX states with master reality (57 rows)“, pak `55018eb` „true up four ticket states“). Je to opakovaná chyba, ne jednorázová.

### 5.4 Phase 8 nikdy nedostala ověření v prohlížeči ani nezávislý review

Zaznamenáno přímo v INDEXu, necituji to zlehčeně — **13 merged ticketů** má v test-planu explicitní řádek „Nešlo ověřit v této session“ (12 výskytů napříč `docs/test-plans/`), protože nebyla k dispozici přihlášená session. Zároveň **každý `docs/review/runs/T-*.md` z toho běhu je self-review** implementující session (reviewer agent byl celý běh nedostupný).

Chybí: manuální průchod všemi 13 na 375 / 768 / 1280 v Chrome **i** WebKit.

### 5.5 Nesloučené větve

| Větev | Co na ní je |
|---|---|
| `feat/T-0180-reactivation-paths` | 2 commity: WIP `Auditable.Reactivate` + plán rozdělení Stripe (T-0182–T-0185). **Obojí má hodnotu a není nikde jinde.** |
| `fix/ci-canonicalize-openapi-spec-hash` | 3 commity, kanonizace OpenAPI JSONu před hashováním. Zastaralé vůči masteru. |
| `fix/ci-oidc-assertion-expiry` | 1 commit, re-login do Azure před Key Vault kroky (AADSTS700024). |
| `ops/dev-diagnostics-workflow` | 1 commit, stahování filesystem logů z App Service. |
| `plan/site-ux-functional-sweep` | 1 commit, plán Phase 8 — už zpracovaný, lze smazat. |

Ostatní ~25 větví je merged a jde uklidit.

### 5.6 Zastaralá dokumentace

- `docs/status/` končí u **sprint-7** (2026-06-02), práce pokračovala až do 2026-08-24. Sprint 8+ nemá záznam.
- `docs/audits/INDEX.md` tvrdí *„no audits run yet“* — přitom UX audit z 2026-08-21 proběhl a je v `docs/review/ux-functional-audit-2026-08-21.md`.
- `docs/HANDOFF.md` mluví o „no code written yet“ — je to discovery dokument, ale jméno svádí k tomu ho číst jako aktuální stav.

### 5.7 Drobnosti

- 12 eslint varování — nepoužité importy (`parsePositiveInt` v 5 admin stránkách, `readString` ve 2, `Alert` ve 2 profilech, `useEffect` v `order-actions.tsx`) + 1 `react-hooks/exhaustive-deps` v [magic-client.tsx:127](../frontend/src/app/(auth)/magic/magic-client.tsx#L127). Pozůstatek po T-0175 refaktoru stránkování.
- SendGrid adaptér zůstal v `Infra.Clients/` i po přechodu na Resend (T-0157). Buď je to vědomý fallback, nebo mrtvý kód — není to nikde napsané.

---

## 6. Jak to rozjet lokálně

```bash
# 1) Postgres 16 na localhost:5432, db makables_dev, postgres/postgres
~/.makables-dev/start-pg.sh          # na tomto stroji; jinak vlastní PG

# 2) migrace + seed (55 makerů, produkty ve všech kategoriích, objednávky ve všech stavech)
dotnet run --project backend/src/Makables.Tools.Seeder -- --migrate
# seed vytiskne sdílené heslo pro všechny účty

# 3) všechny čtyři hosty
pwsh scripts/run-dev.ps1

# 4) frontend
cd frontend && npm install && npm run dev     # http://localhost:3000
```

Frontend míří na porty 5001/5002/5003/5104 defaultně — lokálně nejsou potřeba žádné env proměnné.

### Pasti, které stály čas (nepřeskakovat)

| Past | Projev | Řešení |
|---|---|---|
| Lokálně se **neodesílají e-maily** | registrace zůstane nepotvrzená | vložit řádek do `one_time_tokens` a zavolat `POST /api/v1/auth/confirm-email` |
| `dotnet ef database update` ignoruje appsettings | `28P01` proti `makables_design` | explicitně `ConnectionStrings__Postgres=...` |
| Integrační testy chtějí Docker | selže konstrukce fixture, vypadá to jako rozbitý test | `MAKABLES_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"` — **maintenance** DB, ne `makables_dev` |
| Azurite kontejnery se nevytváří samy | upload spadne | spustit Azurite a ručně založit každý název z `BlobContainer.All` |
| `apiFetch` má 8s default timeout | upload skončí holým „cancelled“, server loguje 499 | multipart helpery musí předat `timeoutMs: 120_000` |
| Starý `next dev` proces | rozbité logo makerů, nula karet v katalogu | `next.config.ts` se čte jen při startu — restartovat |
| Starý `dotnet run` host | bug na jednom hostu a ne na sourozenci | porovnat `ps -o lstart=` s commitem opravy |
| Funkční host se nespustí kvůli DI | „Resend neposílá“, ale v App Insights nula výjimek | zkontrolovat `outbox_event.last_error`, ne Resend |
| `local.settings.json` pro Functions | je gitignorovaný a `run-dev.ps1` Functions host nespouští | Resend klíč je placeholder — ověřit `GET /domains` |

Plné znění: [docs/deployment/local-dev.md](./deployment/local-dev.md).

---

## 7. Doporučené pořadí prací pro přebírajícího

1. **Opravit chybějící cs-CZ klíč** (5.1) — pět minut, uživatelsky viditelné.
2. **Zapojit `check-consistency.mjs` do CI** (5.2) — jinak se stejný typ chyby vrátí.
3. **Srovnat stav ticketů** (5.3) a založit chybějící soubory T-0193, T-0196, T-0182–T-0185; zachránit plánovací commit `768507a` z větve.
4. **Dokončit T-0180** z rozpracované větve, nebo ji vědomě zahodit.
5. **Ruční průchod prohlížečem** přes Phase 8 (5.4) — 13 ticketů, Chrome + WebKit, 375/768/1280.
6. **T-0153** — oživit dev prostředí v Azure, vyřešit cookie doménu, projít celou smyčku. Tohle je jediná věc, která odpoví na otázku „funguje ten marketplace vůbec dohromady?“.
7. **Vytlačit obchodní rozhodnutí** — Q-0030 (právník) a Q-0036 (ČNB) jsou na kritické cestě ke spuštění a ani jedno není inženýrská otázka.

---

## 8. Kde hledat dál

| Chci vědět | Soubor |
|---|---|
| Pravidla, podle kterých se tu píše kód | [CLAUDE.md](../CLAUDE.md) |
| Katalog vzorů (C# i TS) — jediný zdroj pravdy pro *tvary* | [docs/architecture/patterns.md](./architecture/patterns.md) |
| Proč je něco takhle rozhodnuté | [docs/adr/](./adr/) — 28 ADR, klíčové: 0007 (pivot na .NET), 0012 (auth), 0013 (scoping), 0016 (Comgate), 0022 (NSwag), 0027 (Stripe) |
| Doménový model po rolích | [docs/architecture/roles/](./architecture/roles/) — 34 souborů |
| Backlog a stav | [docs/tickets/INDEX.md](./tickets/INDEX.md) |
| Co se musí testovat a kde | [agents/knowledge/testing.md](../agents/knowledge/testing.md) |
| Bezpečnostní pravidla (S-rules) | [agents/knowledge/security-rules.md](../agents/knowledge/security-rules.md) |
| Provozní runbooky | [docs/runbooks/](./runbooks/) — rotace secrets, monitoring, backup/restore |
| Deploy postup | [docs/deployment/deploy-runbook.md](./deployment/deploy-runbook.md) |
| Env proměnné | [docs/deployment/env-vars.md](./deployment/env-vars.md) |
| Uživatelské příběhy | [docs/user-stories/](./user-stories/) — 64 příběhů (25 zákazník, 20 maker, 19 admin) |
| Otevřené otázky | [docs/questions/open.md](./questions/open.md) |
| Blokátory spuštění | [docs/launch-checklist.md](./launch-checklist.md) |

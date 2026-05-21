# Makables — frontend

Next.js 16 (App Router), React 19, Tailwind 4. **Pure presentation layer.**

## What lives here

- Pages and Server Components under `src/app/`
- Reusable components under `src/components/`
- The NSwag-generated TypeScript API client under `src/lib/api-client/` (will be added in Batch 4)
- Client-side helpers (auth state, `apiFetch` wrapper, `Result<T>`) under `src/lib/runtime/`
- i18n catalogs under `src/lib/i18n/`
- Pure utilities (date formatting, money display, validation mirrors) under `src/lib/utils/`

## What does NOT live here

- Business logic — lives in `/backend/` (.NET).
- Database access — there is no database client in this codebase.
- Server-side data fetching against third-party APIs (Comgate, Packeta, ARES, Resend) — all proxied through the backend.

## Status

The frontend was previously coupled to Supabase via `src/lib/supabase/`. That coupling was removed as part of the stack pivot (see [`../docs/adr/0007-stack-pivot-dotnet-backend.md`](../docs/adr/0007-stack-pivot-dotnet-backend.md)).

Until the .NET backend ships endpoints the frontend expects, **most pages will fail to compile or run**. This is intentional: no mocks, no silent fallbacks. Broken paths are loudly visible.

The marketing pages (`(public)/`) that only use `src/lib/demo-data.ts` still render. The hero scene, layout, and UI components all still work.

## Local development

```bash
cd frontend
npm install
npm run dev
```

`http://localhost:3000` — the catalog and product pages will render; everything past the dashboard requires the backend.

## Patterns

Read [`../docs/architecture/patterns.md`](../docs/architecture/patterns.md) Section B for the frontend-specific patterns. Section A covers backend patterns but is useful context.

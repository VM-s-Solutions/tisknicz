# ADR 0024 — SSR auth cookie forwarding through `apiFetch`

- **Status:** accepted
- **Date:** 2026-06-01
- **Supersedes:** none
- **Related:** ADR 0005 (per-audience hosts), ADR 0012 (auth + cookies), ADR 0022 (NSwag pipeline); T-0035 (cookies established); T-0049 review B1 (the case that surfaced it)

## Context

Through Sprint 5 every authenticated frontend surface (`/dashboard/zakaznik/*`, `/dashboard/maker/profil`) was implemented as `'use client' + useEffect` — the browser's cookie jar rides along on the `apiFetch` call's `credentials: 'include'` and the Maker / Customer host sees the audience-scoped session cookie. That works because Client Components run in the browser.

T-0049 (`/dashboard/maker/produkty`) is the first **authenticated Server Component** on the platform. Server Components run on the Node runtime; there is no implicit cookie jar; `credentials: 'include'` is meaningless server-side. Without intervention every server render would hit the Maker host unauthenticated, get back a 401, and either render the wrong error UI or call `notFound()` for what is actually a fully-valid session.

The same gap exists for every future authenticated SSR page (the entire Maker / Customer / Admin dashboard surface as it grows past T-0049).

## Decision

`apiFetch` is the single chokepoint for backend HTTP from the frontend. It now detects the server runtime (`typeof window === 'undefined'`) and, when called for a non-public host, forwards the audience-scoped cookie pair (`makables_access_<host>` + `makables_refresh_<host>`) it reads from `next/headers`'s `cookies()` as a `Cookie` request header. The browser path is unchanged.

Constraints honoured:

- **Audience isolation.** Only the cookies that belong to the host's audience are forwarded. A server render on a Customer dashboard page that incidentally calls the Maker host (it shouldn't, but the safety holds) doesn't leak the Customer session to Maker.
- **No public-host bleed.** The public host is anonymous; cookies are never forwarded there even if the calling Server Component is signed in.
- **Caller override.** A caller-supplied `Cookie` header wins. Tests and one-off rigs stay in control.
- **Graceful fallback.** Outside a request scope (build-time prefetch, unit tests under Vitest) the `next/headers` import throws; the helper swallows that and lets the request go unauthenticated. The backend's 401 then folds to a typed `ApiError`, the route renders its error UI, and the dev sees the surface clearly.

The `cookies()` import is dynamic so the file stays consumable from any environment; the static import would force every consumer into the Server Component runtime contract.

## Consequences

**Positive.**

- Every authenticated Server Component works automatically by calling its `lib/api-client-helpers/*` wrapper — no per-page session plumbing.
- The pattern documents itself in a single chokepoint; the next maker / customer / admin SSR page doesn't have to relearn it.
- The browser path stays identical — no client-side regression.

**Negative.**

- One more reason to keep `apiFetch` as the only allowed backend caller. Direct `fetch()` from a Server Component would still be unauthenticated. The lint surface already flags `fetch` outside `lib/runtime/`; the rule stays load-bearing.
- `lib/runtime/api-fetch.ts` now imports from `lib/auth/session.ts` for the cookie prefixes. Both are runtime-shared modules so the dependency direction is fine, but a future refactor of `session.ts` to depend on `runtime` would create a cycle. Keep `session.ts` runtime-free.

**Out of scope.**

- Token rotation across the boundary. If the access token has expired but the refresh is valid, the Server Component still sees a 401. Two options for later: (1) trigger refresh in middleware (forces a redirect cycle); (2) let the page error UI prompt re-login. Today's flow defaults to (2) which is the same as the existing client surface. Worth a separate ADR / ticket when token-rotation strategy is revisited.
- Replacing cookies with explicit session tokens at the Server Component boundary. That would be a wider auth-architecture change; not needed at MVP.

## Rejected alternatives

- **Convert T-0049 (and every future authenticated page) to client-rendered.** Matches existing precedent but locks the platform out of SSR for any authenticated surface — losing TTFB, SEO for any indexable maker / admin view, and a chunk of the Next 16 mental model. The architectural cost compounds with every page.
- **Render shell SSR, fetch from a route handler proxy.** A Server Component renders the shell and a client child fetches via a Next.js route handler on the same origin (cookies forward by default). Works but adds an extra hop per page and a route handler per endpoint — far more code than the chokepoint extension.
- **A separate `apiFetchServer` helper.** Two entry points encode the runtime split into the call sites; the helper module would still have to remember which to call. The chokepoint detection inside `apiFetch` keeps the API surface to one function.

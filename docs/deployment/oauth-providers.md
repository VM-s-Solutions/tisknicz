# OAuth provider registration — Google + Apple sign-in

Operator steps to register Makables with Google and Apple so the
login/register sign-in buttons work. The backend code is fully wired
(Google T-0026/PR #88, Apple T-0139/ADR 0026); the ONLY missing input is
the provider credentials. Neither option group is `ValidateOnStart` —
hosts boot without them and the buttons fail closed at the provider
until real values land.

Once registered, set the GitHub **environment secrets** below (per
environment: `dev`, `production`) and re-run the deploy workflow — it
pushes them to Key Vault, and the hosts read them as
`Auth__Google__*` / `Auth__Apple__*` app settings via KV references.

| GitHub secret | Key Vault secret | Config key | What it is |
|---|---|---|---|
| `GOOGLE_OAUTH_CLIENT_ID` | `Auth--Google--ClientId` | `Auth:Google:ClientId` | OAuth 2.0 Web client id (`….apps.googleusercontent.com`) |
| `GOOGLE_OAUTH_CLIENT_SECRET` | `Auth--Google--ClientSecret` | `Auth:Google:ClientSecret` | OAuth 2.0 Web client secret |
| `APPLE_SERVICES_ID` | `Auth--Apple--ClientId` | `Auth:Apple:ClientId` | Apple **Services ID** identifier (e.g. `cz.makables.web`) |
| `APPLE_TEAM_ID` | `Auth--Apple--TeamId` | `Auth:Apple:TeamId` | 10-char Team ID (Membership page) |
| `APPLE_KEY_ID` | `Auth--Apple--KeyId` | `Auth:Apple:KeyId` | 10-char Key ID of the Sign in with Apple key |
| `APPLE_PRIVATE_KEY_PEM` | `Auth--Apple--PrivateKeyPem` | `Auth:Apple:PrivateKeyPem` | Full contents of the downloaded `.p8` file (multi-line PEM) |

## Redirect / return URLs (register these EXACTLY)

The OAuth flows start and finish on the **API hosts** (not the web
frontend) — the callback routes live on the shared `AuthController`.
Only the customer and maker audiences use OAuth (admin is rejected
server-side), so register the customer + maker hosts per environment:

**dev**

```
https://app-makables-customer-weu-dev.azurewebsites.net/api/v1/auth/google/callback
https://app-makables-maker-weu-dev.azurewebsites.net/api/v1/auth/google/callback
https://app-makables-customer-weu-dev.azurewebsites.net/api/v1/auth/apple/callback
https://app-makables-maker-weu-dev.azurewebsites.net/api/v1/auth/apple/callback
```

**production** (once the prod RG exists; adjust if custom API domains are mapped)

```
https://app-makables-customer-weu-prod.azurewebsites.net/api/v1/auth/google/callback
https://app-makables-maker-weu-prod.azurewebsites.net/api/v1/auth/google/callback
https://app-makables-customer-weu-prod.azurewebsites.net/api/v1/auth/apple/callback
https://app-makables-maker-weu-prod.azurewebsites.net/api/v1/auth/apple/callback
```

Never point Google and Apple at the SAME redirect URL — the OAuth state
signer's replay containment relies on the two providers keeping
structurally distinct callback routes (ADR 0026 §Defense).

## Google (free, ~15 minutes)

1. <https://console.cloud.google.com> → create (or pick) a project, e.g.
   **Makables**.
2. **APIs & Services → OAuth consent screen** (Google Auth Platform →
   Branding on newer consoles):
   - User type: **External**.
   - App name **Makables**, support email, developer contact.
   - Authorized domain: `makables.cz` (domains must be ones you own —
     `azurewebsites.net` cannot be listed, and does not need to be;
     redirect URIs are registered separately in step 3).
   - Scopes: `openid`, `email`, `profile` (non-sensitive — no
     verification review needed).
   - Publishing status: while in **Testing**, only listed test users can
     sign in — add your own Google account(s) for dev. Switch to **In
     production** before launch (no review required for these scopes).
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**:
   - Application type: **Web application**, name e.g. `makables-dev`.
   - Authorized redirect URIs: the four `google/callback` URLs above
     (dev now; add prod later or keep a separate prod client — separate
     client per environment is cleaner).
   - No "Authorized JavaScript origins" needed (server-side code flow).
4. Copy the **Client ID** + **Client secret** →
   `GOOGLE_OAUTH_CLIENT_ID` / `GOOGLE_OAUTH_CLIENT_SECRET` GitHub
   environment secrets.

Local dev: add `http://localhost:5001/api/v1/auth/google/callback` and
`http://localhost:5002/api/v1/auth/google/callback` to the same client
and put the id/secret in a git-ignored
`appsettings.Development.local.json` (`Auth:Google:ClientId/ClientSecret`).

## Apple (requires the paid Apple Developer Program, $99/yr, ~30 minutes)

Everything happens in <https://developer.apple.com/account> →
**Certificates, Identifiers & Profiles**.

1. **Identifiers → + → App IDs → App**: bundle id e.g. `cz.makables.app`
   (explicit), description **Makables**; enable the **Sign In with
   Apple** capability. This is the "primary App ID" the web flow hangs
   off — it does not need a real iOS app.
2. **Identifiers → + → Services IDs**: identifier e.g. `cz.makables.web`
   (this string IS the OAuth client id → `APPLE_SERVICES_ID`),
   description **Makables Web**. After creating, open it → enable
   **Sign In with Apple** → **Configure**:
   - Primary App ID: the App ID from step 1.
   - Domains and Subdomains: `app-makables-customer-weu-dev.azurewebsites.net`,
     `app-makables-maker-weu-dev.azurewebsites.net` (+ prod hosts later).
   - Return URLs: the four `apple/callback` URLs above (HTTPS required —
     Apple accepts no `http://localhost`; test the Apple flow on the dev
     environment, not locally).
3. **Keys → +**: name e.g. `makables-siwa`, enable **Sign In with
   Apple**, Configure → select the primary App ID → Register →
   **Download the `.p8` file** (one-time download — store it in a
   password manager) and note the **Key ID** shown next to it.
4. **Membership** page → note the 10-char **Team ID**.
5. Set the GitHub environment secrets:
   - `APPLE_SERVICES_ID` = `cz.makables.web`
   - `APPLE_TEAM_ID` = the Team ID
   - `APPLE_KEY_ID` = the Key ID
   - `APPLE_PRIVATE_KEY_PEM` = the FULL `.p8` file contents including
     the `-----BEGIN PRIVATE KEY-----` / `-----END PRIVATE KEY-----`
     lines (GitHub secrets keep newlines; paste as-is).

## After setting the secrets

Re-run **Deploy → dev** (workflow dispatch or push to master). The
"Push external secrets" step replaces the dev boot-stubs with the real
values, and the KV references resolve on the next app-settings sync.
Verify end-to-end: open `/login` on the dev web app → "Pokračovat přes
Google" must land on Google's account chooser and come back logged in
(cookies set on the customer API host); same for Apple.

## Known limitations

- Apple cannot be tested against `http://localhost` (HTTPS-only return
  URLs) — use the dev environment.
- Both providers' callbacks currently end on the backend's JSON
  response with session cookies set; the redirect back to the frontend
  is a known shared follow-up (PR #88 notes).
- The consent screens show the `azurewebsites.net` hostnames until
  custom API domains are mapped; map `api.makables.cz`-style domains
  before launch for a trustworthy consent screen, and update the
  registered URLs then.

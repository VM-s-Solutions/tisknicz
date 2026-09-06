# Custom domain + TLS — binding makables.cz to production

Use this once, after the first production deploy succeeds and before the go-live
data chain in [go-live-bootstrap.md](./go-live-bootstrap.md) sends any email.

---

## Only the frontend needs a custom domain

This is the part that surprises people, so it is worth stating before anything
else. **Two hostnames on one App Service. That is all.**

| Hostname | App Service | Why |
|---|---|---|
| `makables.cz` | `web-makables-weu-prod` | The public site |
| `admin.makables.cz` | `web-makables-weu-prod` | The admin console is the `(admin)` route group in the **same** Next.js app, not a separate deployment |

The four API hosts and the Functions app need **no** custom domain. The browser
never talks to them directly: `NEXT_PUBLIC_API_*_BASE_URL` is set to
`/api-proxy/<host>` and the Next server forwards each call server-side to the
`*.azurewebsites.net` name. That proxy is also why the `Cors:AllowedOrigins`
values rarely matter in practice — those calls reach the API as server-to-server
requests, not browser CORS requests.

**`admin.makables.cz` is cosmetic, not a boundary.** Nothing routes on the Host
header — `middleware.ts` does no host-based routing and there is no canonical-host
redirect. So `admin.makables.cz` serves the entire storefront and
`makables.cz/dashboard/admin` serves the entire admin console. The subdomain is a
convenience for operators, not an access control; the actual gate is the
per-audience JWT the middleware checks. Two consequences worth knowing before you
bind it:

- SEO duplicate content is already mitigated, but only by canonicalisation:
  every `canonicalUrl()` and `robots.ts`'s `host` are built from `SITE_URL`
  (= `NEXT_PUBLIC_SITE_URL` = `https://makables.cz`), so pages served on the
  admin hostname still declare `makables.cz` as canonical. `robots.ts` is
  `allow: '/'` with no host check, so the subdomain *is* crawlable — the
  canonical tag is what keeps it from splitting ranking. Good enough to launch;
  worth knowing it rests on one mechanism.
- `www.makables.cz` is NXDOMAIN and is **not** in scope here. For a consumer
  marketplace that is a gap; decide deliberately rather than by omission.

---

## Order of operations — and it is not the order you would guess

**Deploy production FIRST, then create DNS.** The dependency runs that way round
because both DNS records are derived from the App Service itself:

- the apex `A` record must point at the app's **inbound IP address**, and
- the `asuid` TXT record must contain the app's **custom domain verification ID**.

Neither value exists until the `Microsoft.Web/sites` resource has been
**provisioned**. (Provisioned, not code-deployed — the `bicep` job is enough, and
it runs before the `frontend` job. Step 1 below works against a bare app.)
Microsoft is explicit that the apex cannot be short-circuited: *"For the root
domain, App Service accesses the `asuid` TXT record to verify your ownership."*
No verification ID published, no apex binding — by anyone.

A previous version of the launch plan said the domain had to be bound *before*
the first prod deploy. That was wrong, and both halves of the old reasoning were
wrong:

- `PublicAppUrlsOptionsValidator` only checks that `PublicAppUrls:WebBaseUrl` is a
  well-formed **https** URI — it never resolves the host.
- JWT issuer validation is `ValidIssuer = jwt.Issuer`, a **string comparison**. No
  discovery, no resolution. An unserved issuer string mints and validates tokens
  perfectly well.

So the hosts boot fine with `https://makables.cz` configured against a domain that
does not yet point anywhere. What the domain genuinely gates is **anything
user-visible**:

1. Deploy production. Confirm the smoke job's DB-backed probe passed.
2. Verify the private DB path took — the `nslookup` (must return `10.20.2.x`) and
   `az network private-endpoint-connection list` (must show Approved) checks in
   the "AFTER THE FIRST PROD DEPLOY" block of
   [`infra/bicep/envs/weu.prod.bicepparam`](../../infra/bicep/envs/weu.prod.bicepparam).
3. **This runbook** — DNS, bindings, certificates, OAuth redirect URIs.
4. Only then the go-live data chain. The first maker registration sends a
   confirmation email containing `https://makables.cz/verify?token=…`, and
   **delivered email cannot be recalled**. Bind the domain before that, not after.

---

## Step 1 — Collect the two values from the provisioned app

```bash
RG=rg-makables-weu-prod
APP=web-makables-weu-prod

# Custom Domain Verification ID — the asuid TXT value. Stable per app.
az webapp show -g "$RG" -n "$APP" --query customDomainVerificationId -o tsv

# Inbound IP — the apex A record target.
az webapp show -g "$RG" -n "$APP" --query inboundIpAddress -o tsv
```

## Step 2 — Replace the DNS records at the registrar for makables.cz

> **The apex is not empty. Replace, do not add.** `makables.cz` currently resolves
> to `20.105.232.49` and `20.105.224.126`. The first of those is the shared
> App Service VIP behind `web-makables-weu-dev.azurewebsites.net`
> (`waws-prod-am2-769`) — it has no binding for this hostname and does not serve
> the site. Adding a third `A` record leaves DNS round-robining across two dead
> targets, and DigiCert's domain-control check for the apex can land on an IP with
> no binding and fail issuance. **Delete both existing `A` records first.**

| Record | Name | Value |
|---|---|---|
| `TXT` | `asuid` | the verification ID from step 1 |
| `A` | `@` | the inbound IP from step 1 — **replacing** the two records above |
| `TXT` | `asuid.admin` | the same verification ID |
| `CNAME` | `admin` | `web-makables-weu-prod.azurewebsites.net` |

Three constraints that quietly break certificate issuance or renewal if you get
them wrong:

- **The `admin` CNAME must point directly at `web-makables-weu-prod.azurewebsites.net`.**
  Microsoft is explicit: *"Mapping to an intermediate CNAME value blocks
  certificate issuance and renewal."* Do not chain it through a CDN or a
  registrar-level redirect.
- **Do not put IP restrictions on the frontend app.** For an apex domain,
  *"Both certificate creation and its periodic renewal for a root domain depend
  on your app being reachable from the internet."* The app has none today —
  keep it that way, or the free certificate silently fails to renew months later.
- **If you ever add a CAA record, it must permit DigiCert.** Microsoft: *"For some
  domains, you must explicitly allow DigiCert as a certificate issuer by creating
  a DNS Certification Authority Authorization with the value `0 issue
  digicert.com`."* The domain has no CAA RRset today, so issuance is unblocked —
  but adding one later as a hardening step, without that value, kills renewal
  silently. Same failure class as the two above.

Wait for propagation before continuing (`nslookup -type=TXT asuid.makables.cz`).

## Step 3 — Bind the hostnames, then issue the certificates

Three stages, in this order: a managed certificate can only be created for a
hostname that is **already bound**, and the binding can only be secured once the
certificate **exists**. The portal's Add-custom-domain dialog collapses all three
into one form; **in the CLI they cannot be collapsed**, which is why this is a
loop of three commands.

```bash
RG=rg-makables-weu-prod
APP=web-makables-weu-prod

for HOST in makables.cz admin.makables.cz; do
  # 3a. Bind the hostname (no TLS yet). Fails if DNS is not in place.
  az webapp config hostname add -g "$RG" --webapp-name "$APP" --hostname "$HOST"

  # 3b. Issue the free App Service managed certificate for it, keeping the
  #     thumbprint from the create call rather than re-querying for it.
  #     NOTE: `az webapp config ssl create` is flagged Preview in the CLI.
  #     If it has moved by the time you run this, check `az webapp config ssl --help`.
  THUMB=$(az webapp config ssl create -g "$RG" --name "$APP" \
    --hostname "$HOST" --query thumbprint -o tsv)

  # 3c. Secure the binding with the new certificate (SNI). --hostname is
  #     optional but this app has two, so name the one being bound.
  az webapp config ssl bind -g "$RG" --name "$APP" --hostname "$HOST" \
    --certificate-thumbprint "$THUMB" --ssl-type SNI
done
```

The prod plan is `P1v3` ([`weu.prod.bicepparam`](../../infra/bicep/envs/weu.prod.bicepparam)),
which satisfies the managed-certificate tier requirement (Basic, Standard,
Premium or Isolated).

## Step 4 — Re-register the OAuth redirect URIs

**Do not skip this, and do it before announcing the domain.** The Google/Apple
`redirectUri` is built at click time from `window.location.origin`, so cutting the
domain over silently changes it and every social sign-in fails with
`redirect_uri_mismatch` until the new URIs are registered.

Because the admin subdomain serves the same login pages, there are **three**
origins in play, not one. Add to both the Google Cloud console and the Apple
Service ID (keep the `azurewebsites.net` entries until the cutover is proven):

```
https://makables.cz/api-proxy/customer/api/v1/auth/google/callback
https://makables.cz/api-proxy/maker/api/v1/auth/google/callback
https://admin.makables.cz/api-proxy/customer/api/v1/auth/google/callback
```

…and the matching `apple/callback` forms. See
[oauth-providers.md](../deployment/oauth-providers.md) for the console walkthrough.

## Step 5 — Verify

```bash
curl -sS -o /dev/null -w '%{http_code} %{ssl_verify_result}\n' https://makables.cz/
echo "curl exit: $?"
curl -sS -o /dev/null -w '%{http_code} %{ssl_verify_result}\n' https://admin.makables.cz/
echo "curl exit: $?"
```

Both must print `200 0` **and** `curl exit: 0`.

**The exit code is the real TLS signal, not `ssl_verify_result`.** A certificate
failure aborts the transfer before any response, so you get `000` and a non-zero
exit — and on the Windows/schannel curl build `ssl_verify_result` prints `0` even
for an expired certificate. `200 0` is a valid pass assertion; a *failure* shows
up in the exit code, so check it.

Use the bare hostnames, not `/dashboard/admin`: that prefix is `guarded: true` in
`route-audience.ts`, so an unauthenticated request is redirected to
`/admin/login` and returns **307**, not 200.

Then confirm the app is actually serving on the new hostname rather than
redirecting: `NEXT_PUBLIC_SITE_URL` and `PublicAppUrls:WebBaseUrl` are already
`https://makables.cz`, so links generated after this point are live.

---

## Why this is a runbook and not Bicep

Deliberate, and worth recording so nobody "fixes" it later:

- **The DNS records cannot exist before the app is provisioned** (step 1 derives
  both from the app), so hostname bindings in `main.bicep` would fail on the
  very first production deploy and only succeed on a second — every prod deploy
  would be red until someone had done the manual half anyway.
- **The three stages are circular in a single template.** The certificate depends
  on the binding; the secured binding depends on the certificate. Expressing that
  in one Bicep pass needs nested deployments to break the cycle, for an operation
  that happens once per environment.
- **Manual bindings are expected to survive redeploys — verify this once.**
  `hostNameSslStates` is a property on `Microsoft.Web/sites` itself, and
  `web-app.bicep` redeploys that resource on every prod run, so the usual
  "incremental mode leaves undeclared *resources* alone" guarantee does not
  strictly cover it. In practice the App Service RP preserves bindings when the
  property is absent from the request, and no documented behaviour says
  otherwise — but this is an assumption, not a citation. **After the next prod
  deploy following the binding, run `az webapp config hostname list -g
  rg-makables-weu-prod --webapp-name web-makables-weu-prod` and confirm both
  hostnames are still there.** If they are not, the fix is the `customDomains`
  param below, not a manual re-bind.

If you later want it codified — for rebuilding the resource group from scratch —
the shape is a `customDomains` array param on `main.bicep`, empty by default and
populated only after DNS exists, feeding a two-module binding/certificate pair.
That is a real option, not a rejected one; it is simply not worth writing before
the domain has ever been bound once.

## Renewal

App Service managed certificates renew automatically. The things that break
renewal are exactly the constraints in step 2 — an intermediate CNAME, IP
restrictions on the apex, or a CAA record that omits DigiCert. All fail silently,
months later. If the certificate ever lapses, check those before anything else.

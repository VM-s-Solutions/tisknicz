# Go-live bootstrap — first admin, first maker, first product

Use this when bringing a **fresh environment** (most importantly production) from
"deployed and migrated" to "a customer can place an order". It is the data half of
go-live; the infra half is `docs/deployment/deploy-runbook.md`.

> **This chain contains an external party and cannot be compressed into a deploy
> window.** A real maker has to register, pass ARES, and create a real product
> before any customer order can exist. Schedule it, with owners, ahead of launch —
> not on the day.

---

## Why a fresh database needs this at all

A migrated database has reference data (country `CZ`, the six categories, the email
templates) and **zero people**. It cannot start itself, for three reasons that all
have to be resolved in order:

1. `Register` refuses `UserRole.Admin` outright
   (`Features/Auth/Register.cs`), so no admin can sign up.
2. Every admin-management use case requires an existing admin, so there is no
   in-product way to make the first one.
3. `CreateOrder` refuses an unverified maker, and only an admin can verify one.

`Makables.Tools.Seeder` does not solve this: it manufactures an admin and
pre-verified makers, but hard-refuses any target whose host or database name
contains `prod`, and every identity it creates is `@makables.test`.

---

## Step 1 — Create the first admin

`Makables.Tools.AdminBootstrap` is a one-shot operator tool. It refuses to run when
an active admin already exists, so it is a bootstrap rather than a standing
privilege-escalation path.

**Prerequisite:** a network path to the target Postgres. On production that means
the private endpoint / VNet integration, or a break-glass firewall rule for the
operator's IP for the duration of this step. See `docs/launch-checklist.md`.

```bash
cd backend/src
```

**Keep both credentials out of shell history.** The connection string contains
`Password=`, and an inline env-var prefix still lands in `~/.bash_history` (or
`ConsoleHost_history.txt`). Source it instead, and disable history for the step:

```bash
set +o history                      # bash; in PowerShell: Set-PSReadLineOption -HistorySaveStyle SaveNothing
export ConnectionStrings__Postgres="$(az keyvault secret show \
  --vault-name kv-makables-weu-prod --name ConnectionStrings--Postgres \
  --query value -o tsv)"
```

Then run the tool. It reads the admin password from **stdin**, never argv, so it
cannot reach history or another user's `ps` output:

```bash
dotnet run --project Makables.Tools.AdminBootstrap -- \
  --email ops@makables.cz \
  --name "Ops" \
  --confirm-database makables_prod
# → prompts: Admin password (input hidden):
```

Afterwards: `unset ConnectionStrings__Postgres` and `set -o history`.

**`--confirm-database` is mandatory and must match** the database the connection
string actually resolves to. It is the target guard, and it replaces a
host-is-localhost check *deliberately*: reaching a private production Postgres
means an SSH tunnel, which makes the target `localhost:5432` — so a localhost
check would wave through the one target that most needs confirming, while
printing a reassuring "localhost". Naming the database proves the operator knows
where they are.

A scripted break-glass step works the same way, with the password piped:

```bash
printf '%s' "$ADMIN_PASSWORD" | dotnet run --project Makables.Tools.AdminBootstrap -- \
  --email ops@makables.cz --name "Ops" --confirm-database makables_prod
```

Prefer a published binary run from a jump box or Cloud Shell over `dotnet run`
from a developer laptop.

| Exit code | Meaning |
|---|---|
| `0` | Admin created. An `admin.bootstrap` entry is in the admin audit log. |
| `1` | Refused for a safety reason — an active admin already exists, the email is taken (including a different casing, or one held by a soft-deleted account), `--confirm-database` is missing or does not match, or the database rejected the insert as a duplicate. **Nothing was written.** |
| `2` | Bad input — missing/malformed email or name, a password under 12 characters, or no `ConnectionStrings__Postgres` set. |
| _stack trace_ | A connection string that is present but unusable (wrong host, refused, bad credentials) surfaces as an unhandled exception rather than an exit code. That is deliberate — the failure is environmental and the operator needs the detail — and it is pinned by `AdminBootstrapCompositionRootTests`. |

The tool echoes the resolved host and database before writing, so a wrong
connection string is visible rather than discovered afterwards.

The tool is **not** a security boundary: anyone holding the production
connection string could insert an admin row by hand. Its guards stop a correct
operator making a wrong move. The real control is database network and
credential isolation.

**Password floor is 12 characters**, deliberately higher than the app's own 10-char
registration minimum: this account can refund money, erase users and change country
configuration, and it is typed once by an operator rather than remembered by a
customer. Change it after first sign-in.

**If every admin has been deactivated** the tool becomes available again — that is
intentional. A platform with only soft-deleted admins has no reachable console,
which is the situation this tool exists to resolve.

## Step 2 — A real maker registers

Self-service, through the **public** host — `RegisterMakerController` lives in
`Makables.Web.Public` (its own doc notes the route must not appear on the
customer/maker/admin hosts), and the page is `(auth)/register/maker`. An operator
tailing the maker host during cutover will see nothing. The maker supplies their
IČO; ARES is queried
and the company snapshot stored. They must confirm their email — which requires the
outbox to be draining, so **verify email delivery works before inviting anyone**
(`docs/runbooks/monitoring.md` §outbox).

## Step 3 — Admin verifies the maker

Admin console → Makers → verify. Until this happens the maker is invisible: the
public catalog filters on `Maker.IsVerified` (list, profile-by-slug and
product-by-id alike), and `CreateOrder` rejects them with `maker.notVerified`.

> Before the verification gate landed, an unverified maker WAS publicly listed and
> their products reachable, while checkout rejected them at the last step — the
> storefront and the trust model disagreed. If you are reading this during an
> incident where makers appear but cannot be ordered from, check that gate first.

## Step 4 — The maker creates a product

Maker dashboard. Note there is currently **no draft/published state**: a product is
public the instant it is created (it inherits only the soft-delete flag). A maker
preparing a catalogue does so in the open.

## Step 5 — Verify the money path end to end

Follow `docs/test-plans/T-0153-e2e-walk.md`. On production this is the **first**
exercise of the real Comgate path, because non-production environments run the
`DevPaymentProvider` bypass and never call the gateway.

Confirm before starting:

- `COMGATE_WEBHOOK_ALLOWED_IPS` is set for the environment. The allowlist is
  fail-closed — while it is empty every callback is rejected with 401 and no order
  can reach `Paid`.
- The Comgate notification URL points at the **public API host directly**, not at
  `makables.cz/api-proxy/...`. Through the frontend proxy the last forwarded hop is
  the frontend egress IP and the callback 401s permanently. Fix the URL — never add
  that egress IP to the allowlist.

---

## Verification (manual — staging dry-run)

Not yet executed. Run the whole chain against a disposable environment and record
the result here:

- [ ] Bootstrap refuses when `--confirm-database` is missing, and when it names
      a different database than the connection string resolves to.
- [ ] Bootstrap creates an admin; the `admin.bootstrap` audit entry is present.
- [ ] Re-running the bootstrap refuses with exit `1` and writes nothing.
- [ ] An unverified maker is absent from `/katalog`, from their own slug URL, and
      from product-detail-by-id.
- [ ] After admin verification the maker and their products appear.
- [ ] A customer completes checkout and the order reaches `Paid`.

# Function-key rotation + poison-queue monitoring

T-0029 sec reviewer follow-up. Documents the ops posture around the two
T-0029 Functions (`ProcessOutboxFunction` HTTP trigger + the
`<send-email>-poison` queue that the queue runtime owns).

## ProcessOutbox HTTP trigger

- Route: `POST /api/outbox/process`
- Auth: `AuthorizationLevel.Function` — caller must present `x-functions-key`
  in the request header (or `?code=` query string — prefer header so the
  key doesn't end up in proxy / nginx access logs).
- Caller: only the admin operations dashboard ("Process outbox now" button),
  built in Phase 5+. Until then, ops invoke manually with `curl`.

### Key rotation

- The function host exposes two keys per function (a "primary" and "secondary")
  plus host-wide "master" + "default" keys. We use the per-function key only.
- **Rotation cadence: every 90 days.** Rotate in the Azure portal under
  Function App → Functions → ProcessOutboxHttp → Function Keys → Rotate.
- After rotation, update the secret in Key Vault (`functions-processoutbox-key`)
  and re-deploy the admin dashboard so it picks up the new value. The two-key
  primary/secondary pattern lets you rotate without downtime: switch the dashboard
  to the secondary key, rotate the primary, then swap back.
- On suspected compromise, rotate immediately and audit App Insights for
  `ProcessOutboxHttp` invocations from unexpected IPs.

### Rate-limit considerations

`AuthorizationLevel.Function` does not include rate-limiting. A leaked key
allows unlimited "process now" calls — each call:

- One indexed Postgres `SELECT` from `outbox_event` (cheap, ≤50 rows).
- Up to 50 Azure Storage Queue publishes.
- One DB `SaveChangesAsync`.

Worst-case cost during a leak: SendGrid bills for emails. Mitigation backlog:

- **Short-term**: Application Insights alert on `ProcessOutboxHttp invocations / hour > N`.
- **Medium-term**: APIM in front of the Function App with a rate-limit policy keyed on the function key.
- **Long-term**: replace function key with admin-bearer-JWT (re-uses the existing JWT issuer; couples to the admin login).

## `<send-email>-poison` queue

Azure Storage Queue routes a message to `<queue>-poison` after `maxDequeueCount`
(currently 5) failed `SendEmailFunction` invocations.

In the T-0029 design, the only paths that throw are:
- An empty queue message (`SendEmailFunction` throws — see CQ reviewer n-2).
- Genuinely unexpected exceptions (DB connection lost, etc.).

Classified failures (`Transient` / `Permanent` / etc.) do NOT throw — the outbox
row owns the retry budget. So `<send-email>-poison` should be effectively empty
in steady state.

### Monitoring

- **Alert**: Azure Monitor metric alert on `QueueMessageCount > 0` for
  `<send-email>-poison`, severity 2 (warning).
- **Runbook**: when the alert fires:
  1. Inspect the message body in the Azure portal → Storage → Queues → `<send-email>-poison`.
  2. The body is an outbox event id (or empty if reviewer n-2 triggered).
  3. Look up the row: `SELECT * FROM outbox_event WHERE id = '<id>';` — its `last_error_kind`
     / `last_error_code` will explain whether it's stalled too.
  4. If the underlying defect is fixed and the row is still pending, manually
     re-enqueue the id to `<send-email>` to retry.
  5. If the row is permanently broken, use the admin `Acknowledge` action
     (T-0011 entity method) so the outbox row stops appearing in the stalled
     list, then delete the poison message.

### Re-poison protection

A poisoned id sits in `<send-email>-poison` forever, but the outbox row is
parked for `OutboxDispatcher.HandoffParkMinutes` minutes. After that window the
dispatcher re-publishes to the live queue, the same defect triggers another
5-retry burst, and the row lands in the poison queue **again** — but with the
SAME message body, not a stacked duplicate. Storage Queues do not enforce
uniqueness, so the queue will have multiple poison messages with the same id.
Each refers to the same outbox row.

The intended remediation is admin `Acknowledge` on the row, which prevents
future re-publishing. This is the only thing that breaks the poison loop.

Follow-up ticket idea: a `<send-email>-poison` consumer Function that reads
the body, calls `OutboxEvent.RecordFailure(Permanent, "queue.poisoned", null)`
so the row stalls automatically on first poisoning. Not in scope for T-0029.

## Storage account connection string

`OutboxQueues:ConnectionString` is bound from configuration. In production it
is a Key Vault reference (`@Microsoft.KeyVault(...)`); the function host
substitutes the value at boot. The singleton `QueueClient` is constructed
once with that value — **rotating the connection string requires a host
restart**, not a config reload. Document this in the same Key Vault rotation
runbook as the function key.

## T-0031 Mapbox autocomplete proxy — IP-bucket prerequisites

The `addresses-autocomplete` partitioned rate-limit policy keys on
`ClaimTypes.NameIdentifier` when present (Makables's JWT issuer mirrors
the OAuth `sub` claim into `NameIdentifier`, so "per `sub`" and "per
`NameIdentifier`" are equivalent here), falling back to remote IP for
unauthenticated requests. **The endpoint is `[Authorize]` today**, so the
IP path is unreachable from outside.

**Before opening the endpoint to anonymous traffic** (e.g. registration form),
two prerequisites MUST be met:

1. **`UseForwardedHeaders` middleware wired in `UseMakablesPipeline`** so
   `HttpContext.Connection.RemoteIpAddress` reflects the real client, not the
   ingress proxy. Without this every external caller shares a single IP
   bucket and an attacker trivially DoSes every other anonymous user. ASP.NET
   Core's `ForwardedHeadersOptions` should restrict known proxy IP/Network so
   header injection can't spoof the client IP.
2. **A regression test or analyzer rule** that asserts `[Authorize]` stays on
   `AddressAutocompleteController` until step 1 lands.

Tracked as a follow-up ticket; this note is the evidence that the
prerequisites are known + documented.

## T-0031 Mapbox access token

`Mapbox:AccessToken` is a Key Vault reference in production. The adapter
sends it as `Authorization: Bearer {token}` on every request — NOT as a
`?access_token=` query parameter — so the OTel HttpClient instrumentation
doesn't capture it into App Insights span attributes. Rotation: cycle the
Key Vault secret + restart the Function App / Web Apps (the named HttpClient
caches `IOptions<MapboxOptions>.Value` at first resolve, so a config refresh
alone won't pick up a new token).

`Mapbox:BaseUrl` is validated to require `https://` scheme at `ValidateOnStart`.
Hostname allow-list (restrict to `api.mapbox.com`) is a future hardening
ticket; today the prod config is pinned in deploy templates and Key Vault
reads are read-only for the running app principal.

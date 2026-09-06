#!/usr/bin/env bash
# Assert the Azure Functions host is Running with NO functions in error.
#
# WHY THIS EXISTS ALONGSIDE THE /api/health PROBE
# /api/health proves the site is up, the worker process is running and every
# ValidateOnStart options check passed. It does NOT prove the app is working:
#
#   * Container validation (ValidateOnBuild / ValidateScopes) is enabled only
#     when IsDevelopment(). The deployed host runs as Production, so an
#     unresolvable handler dependency does not stop startup — it surfaces at the
#     first invocation, long after the deploy went green.
#   * A trigger whose %Setting% binding does not resolve is reported by the host
#     as "in error" while the host keeps serving every other function. That is
#     exactly the outbox-never-drains failure: ProcessOutboxTimer silently never
#     fires and no transactional email is ever sent, with /api/health green.
#
# /admin/host/status is the authoritative answer. The master key is fetched over
# ARM (control plane) and sent as a HEADER, never in the query string, so it
# cannot land in a log line, a redirect or a proxy access log.
#
# Usage: assert-functions-host-healthy.sh <resource-group> <function-app-name>
# Requires: az (logged in), curl, python3.
set -euo pipefail

rg=${1:?usage: assert-functions-host-healthy.sh <resource-group> <function-app-name>}
app=${2:?usage: assert-functions-host-healthy.sh <resource-group> <function-app-name>}

key=$(az functionapp keys list -g "$rg" -n "$app" --query masterKey -o tsv 2>/dev/null || true)
if [ -z "$key" ] || [ "$key" = "null" ]; then
  # Deliberately fatal rather than a warning: the cheap /api/health probe has
  # already passed by the time this runs, so this is not a cold-start race — it
  # is a real inability to verify the thing the gate exists to verify.
  echo "::error::could not read the master key for $app (resource group $rg). The Functions host may be fine, but this gate cannot confirm it is free of functions in error."
  exit 1
fi

status=$(curl -sS --max-time 30 -H "x-functions-key: ${key}" \
  "https://${app}.azurewebsites.net/admin/host/status" || true)

APP="$app" python3 - "$status" <<'PY'
import json, os, sys

app = os.environ["APP"]
raw = sys.argv[1] if len(sys.argv) > 1 else ""
try:
    s = json.loads(raw)
except json.JSONDecodeError:
    print(f"::error::{app} /admin/host/status did not return JSON (got: {raw[:200]!r}). "
          f"The host is not serving its admin API.")
    sys.exit(1)

state = s.get("state")
errors = s.get("errors") or []
print(f"  host state={state} version={s.get('version')} errors={len(errors)}")

if state != "Running":
    print(f"::error::{app} host state is {state!r}, not 'Running'. Timers and queue triggers are not "
          f"executing, so the outbox will not drain and no transactional email will be sent.")
    sys.exit(1)

if errors:
    for e in errors:
        print(f"  - {e}")
    print(f"::error::{app} reports functions IN ERROR (listed above). A trigger whose %Setting% binding "
          f"does not resolve never fires, so the outbox can stop draining silently while /api/health "
          f"stays green. Check the app settings named in those errors.")
    sys.exit(1)

print("  all functions indexed without error")
PY

#!/usr/bin/env bash
# Pre-flight: is the blob storage SKU in a .bicepparam actually entitled on this
# subscription, in that region?
#
# WHY
# Storage SKU entitlement is SUBSCRIPTION-scoped, and this subscription is
# already offer-restricted for Postgres Flexible Server in westeurope (see
# weu.prod.bicepparam). Without this check a restriction surfaces inside the
# `blob` module ~20 minutes into a deploy — after the App Service Plan, App
# Insights, the VNet and a fresh Postgres server exist, on a first-ever
# production deploy that cannot be cleanly torn down because the Key Vault has
# already taken a 90-day name lock.
#
# FAIL-OPEN ON "CANNOT DETERMINE", FAIL-CLOSED ON "DEFINITELY BAD"
# This is an accelerator, not the gate — ARM is the gate. So a definite answer
# of "not offered" or "restricted" exits 1, but an inability to ask (no
# subscription-scope read for the deploy principal, provider not registered, a
# transient 5xx) WARNS and exits 0. Failing closed there would block a deploy
# that would otherwise have succeeded, which is the exact class of own-goal this
# script exists to prevent. The deploy principal is provisioned Contributor
# scoped to the RESOURCE GROUP (docs/deployment/infra-migration-2026-07.md), and
# this query needs Microsoft.Storage/skus/read at SUBSCRIPTION scope, so the
# 403 path is a realistic everyday case, not a corner.
#
# Usage: preflight-storage-sku.sh <bicepparam-path> <subscription-id>
# Requires: az (logged in), python3 (ships inside the az CLI image).
set -euo pipefail

pf=${1:?usage: preflight-storage-sku.sh <bicepparam-path> <subscription-id>}
sub=${2:?usage: preflight-storage-sku.sh <bicepparam-path> <subscription-id>}

# sed, not `grep -oP`: PCRE mode is unavailable on non-UTF-8 locales and on
# busybox, and a missing feature here would block every deploy. Same reasoning
# that removed the jq dependency from the smoke probe.
sku=$(sed -n "s/^param blobStorageSku = '\([^']*\)'.*/\1/p" "$pf")
loc=$(sed -n "s/^param location = '\([^']*\)'.*/\1/p" "$pf")

# An unset blobStorageSku is CORRECT for dev — it falls through to main.bicep's
# Standard_LRS default. Mirror that default rather than treating it as an error,
# so this script rehearses on every dev deploy instead of only ever running on
# the one production deploy it is meant to protect.
: "${sku:=Standard_LRS}"

if [ -z "$loc" ]; then
  echo "::warning::could not read 'param location' from $pf — skipping the storage SKU pre-flight. ARM remains the gate."
  exit 0
fi

echo "pre-flight: is $sku offered for storageAccounts in $loc on this subscription?"

# Ask for every entry with this SKU name and let python do the matching. Two
# reasons not to filter by region in JMESPath: the field is `locations` (a
# plural ARRAY), not a scalar `location` — an earlier version of this check got
# that wrong and would have failed EVERY production deploy with a confidently
# incorrect "not offered" message — and its casing is not contractual (the REST
# reference samples lowercase, live responses have been seen uppercase).
# JMESPath has no lower(), so case-folding happens in python.
if ! raw=$(az rest --method get \
      --url "https://management.azure.com/subscriptions/${sub}/providers/Microsoft.Storage/skus?api-version=2023-05-01" \
      --query "value[?name=='${sku}']" -o json); then
  echo "::warning::could not query Microsoft.Storage/skus (most likely the deploy principal lacks subscription-scope read, or the provider is not registered). Skipping the pre-flight — ARM remains the gate."
  exit 0
fi

SKU="$sku" LOC="$loc" python3 - "$raw" <<'PY'
import json, os, sys

sku, loc = os.environ["SKU"], os.environ["LOC"].strip().lower()
try:
    entries = json.loads(sys.argv[1] or "[]") or []
except json.JSONDecodeError:
    print("::warning::Microsoft.Storage/skus returned unparseable JSON — skipping the pre-flight. ARM remains the gate.")
    sys.exit(0)

# blob.bicep creates kind StorageV2 storage accounts. Restrictions can differ
# per kind, so pin it rather than taking the first entry that happens to match
# the name.
matches = [
    e for e in entries
    if e.get("resourceType") == "storageAccounts"
    and e.get("kind") == "StorageV2"
    and any(str(l).strip().lower() == loc for l in (e.get("locations") or []))
]

if not matches:
    kinds = sorted({e.get("kind") for e in entries if e.get("resourceType") == "storageAccounts"})
    print(f"::error::{sku} is not offered for StorageV2 storage accounts in {loc} on this subscription. "
          f"Kinds returned for this SKU name: {kinds or 'none'}. Pick a SKU that is offered (ADR 0023 §7 "
          f"records why GZRS was chosen) or request the offer, then update the bicepparam.")
    sys.exit(1)

restricted = [r for m in matches for r in (m.get("restrictions") or [])]
if restricted:
    print(json.dumps(restricted, indent=2))
    reasons = sorted({r.get("reasonCode", "unknown") for r in restricted})
    print(f"::error::{sku} is RESTRICTED for this subscription in {loc} (reasonCode: {', '.join(reasons)}). "
          f"The deploy would have failed inside the blob module after Postgres and the VNet were created.")
    sys.exit(1)

print(f"  {sku} entitled for StorageV2 in {loc} — no restrictions")
PY

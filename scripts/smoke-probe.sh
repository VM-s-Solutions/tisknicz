#!/usr/bin/env bash
# Poll a URL until it returns 200 (optionally containing a substring), or a
# WALL-CLOCK deadline passes.
#
# WHY A DEADLINE AND NOT AN ATTEMPT COUNT
# The smoke job used `for attempt in $(seq 1 N)` with a sleep, which makes the
# real worst case N * (curl --max-time + sleep) — easy to under-estimate by
# counting only the sleeps. It had drifted to ~41 minutes of probes inside a
# 15-minute job budget, so the failure mode was a GitHub-cancelled job rather
# than the carefully worded ::error:: the gate's value depends on. A deadline
# makes each step's worst case exactly the number written next to it, so the
# job budget can be computed by adding them up.
#
# Usage: smoke-probe.sh <url> <deadline-seconds> [expect-substring] [label]
set -euo pipefail

url=${1:?usage: smoke-probe.sh <url> <deadline-seconds> [expect-substring] [label]}
budget=${2:?usage: smoke-probe.sh <url> <deadline-seconds> [expect-substring] [label]}
expect=${3:-}
label=${4:-$url}

# Bounded so a hung TCP connect cannot eat the whole budget in one attempt.
per_request_timeout=${SMOKE_REQUEST_TIMEOUT:-20}
interval=${SMOKE_INTERVAL:-10}

deadline=$(( $(date +%s) + budget ))
attempt=0
last="(no response)"

echo "probing $label (up to ${budget}s)"
while [ "$(date +%s)" -lt "$deadline" ]; do
  attempt=$((attempt + 1))
  body=$(curl -sS --max-time "$per_request_timeout" -w $'\n%{http_code}' "$url" 2>/dev/null || printf '\n000')
  code=$(printf '%s' "$body" | tail -n1)
  payload=$(printf '%s' "$body" | sed '$d')

  if [ "$code" = "200" ]; then
    if [ -z "$expect" ]; then
      echo "  up after ${attempt} attempt(s)"
      exit 0
    fi
    # grep, not jq: a missing jq on the runner would fail this CLOSED and block
    # every deploy, and the assertion does not need a JSON parser.
    if printf '%s' "$payload" | grep -q "$expect"; then
      echo "  up after ${attempt} attempt(s), body contains '${expect}'"
      exit 0
    fi
    last="HTTP 200 but body did not contain '${expect}'"
  else
    last="HTTP ${code}"
  fi

  remaining=$(( deadline - $(date +%s) ))
  [ "$remaining" -le 0 ] && break
  echo "  attempt ${attempt}: ${last} — retrying (${remaining}s left)"
  sleep "$(( interval < remaining ? interval : remaining ))"
done

echo "::error::${label} never satisfied the probe within ${budget}s (last: ${last})"
exit 1

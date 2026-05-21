---
id: T-NNNN
title: <short imperative title>
status: draft   # draft | ready | in_progress | in_review | qa | done | blocked
size: S | M | L
owner: <agent name when in_progress>
created: YYYY-MM-DD
updated: YYYY-MM-DD
depends_on: [T-NNNN, T-NNNN]
blocks: [T-NNNN]
user_stories: [US-customer-NNNN, US-maker-NNNN]
adrs: [0001, 0007]
phase: 1 | 2 | 3 | 4
---

# T-NNNN — <Title>

## Context
One paragraph: why this ticket exists, what user value it delivers.

## Scope
Bulleted list of what this ticket changes. Be explicit.

## Out of scope
What this ticket does NOT do (especially if a reader might expect it to).

## Acceptance criteria
Format: Given / When / Then.

- **AC-1** Given <context>, when <action>, then <observable outcome>
- **AC-2** ...

## Technical notes
Implementation hints, gotchas, links to ADRs.

## Files touched (expected)
- `src/...`
- `supabase/migrations/...`

## Test plan reference
`docs/test-plans/T-NNNN.md`

## Status log
- YYYY-MM-DD `draft → ready` by PM
- YYYY-MM-DD `ready → in_progress` by PM, owner BE
- ...

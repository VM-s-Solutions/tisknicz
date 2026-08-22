---
id: T-0188
title: Make the hero camera frame-rate independent (Safari renders at half Chrome's rate)
status: in_review
size: S
owner: claude
created: 2026-08-22
updated: 2026-08-22
depends_on: []
blocks: []
user_stories: []
adrs: []
phase: 8
manual_steps: []
security_touching: false
layers: [frontend]
---

# T-0188 — Make the hero camera frame-rate independent

## Context

Operator report: *"mam pocit ze to laguje jen na safari, ve chromu to jede dobre"* —
and, when asked to narrow it, *"všude, i bez scrollování."*

Measured 2026-08-22 by driving **real Safari** (safaridriver/WebDriver) and
**real Chrome** (Playwright `channel: 'chrome'`) against the deployed dev site
on the reporter's ProMotion display:

| | Safari | Chrome |
|---|---|---|
| idle refresh rate | **61 Hz** | **121 Hz** |
| scroll frame p95 | 18 ms | 9.1 ms |
| dropped frames | **0 / 207** | **0 / 417** |
| client-side `<Link>` nav | 21 / 85 / 57 / 61 ms | 21 / 161 / 59 / 58 ms |
| input → painted frame | 33 ms (2 frames @60) | 16.7 ms (2 frames @120) |

**Safari is not janky — it is running at exactly half the frame rate, frame
perfect.** Every measurement that is not refresh-rate-bound (load, navigation,
input handling) is equal or better in Safari. So the "everywhere, even without
scrolling" feeling is the display cadence, not the app, and no code change can
raise it.

But that asymmetry exposed **one real defect**. `CameraRig` in the WebGL hero
smoothed toward the pointer with a *per-frame* constant:

```
state.camera.position.x += (targetX - state.camera.position.x) * 0.03;
```

Applied 121×/s in Chrome that is a ~0.28 s time constant; applied 61×/s in
Safari it is ~0.55 s. **The hero camera genuinely trails the pointer twice as
far behind in Safari** — visible lag, on the first thing a visitor touches,
present in Safari and absent in Chrome. A code-wide sweep found this is the
only frame-rate-dependent animation in the repo (no other `requestAnimationFrame`
loops exist outside the hero).

## Scope

- `frontend/src/lib/motion/frame-rate.ts` (new) — `smoothingFactor(perFrameAt60, delta)`
  rescales a 60 fps-authored smoothing constant to the frame's real duration,
  clamping delta so a backgrounded tab resumes without the camera teleporting.
- `frontend/src/components/shared/hero-scene.tsx` — `CameraRig` takes `delta`
  and applies the rescaled factor.
- Unit tests for the smoothing math.

## Alternatives Considered

- **Leave it and lower the constant** — still wrong at 144 Hz, and it would make
  Chrome sluggish to fix Safari. Rejected: it trades one browser for another.
- **Cap the hero at 60 fps everywhere** so both browsers match — makes Chrome
  worse to hide a bug rather than fixing it. Rejected.
- **Remove the pointer-follow camera entirely** — defensible against the recorded
  "static by default" design preference, but it changes an approved design
  (2026-07-06) and is a design call, not a bug fix. Rejected for this ticket.

## Out of scope

- The 61 Hz vs 121 Hz difference itself. That is macOS Safari behaviour on a
  ProMotion display, not something the page can influence.
- Hero cost reduction (16 128 GL_LINES segments/frame, 230 kB gz of three.js,
  `antialias: true` on a 2.0 Mpx canvas in Safari). Real numbers, but Safari
  sustained a full 63 Hz with the scene running and reported no errors, so it
  is not the reported defect. Worth its own ticket if the payload matters.
- The `backdrop-filter: blur()` on the sticky navbar. Suspected up front as a
  classic Safari scroll-jank source; measurement cleared it — 0 dropped frames
  scrolling `/katalog` and `/jak-to-funguje` in real Safari.

## Acceptance criteria

- **AC-1** Given the hero is mounted, when the frame rate is 60 Hz or 120 Hz,
  then the camera converges on the pointer over the same wall-clock time.
- **AC-2** Given a tab was backgrounded and returns a multi-second delta, when
  the next frame runs, then the camera eases rather than teleporting.
- **AC-3** Given the fixed build, when `/` is loaded in real Safari and real
  Chrome, then the hero canvas mounts with a live WebGL context and no errors.

## Technical notes

Standard exponential-smoothing rescale: `1 - (1 - k)^(delta * 60)`. At
`delta = 1/60` it returns `k` exactly, so the tuned feel at 60 fps is preserved
and every other rate is matched to it.

## Files touched (expected)

- `frontend/src/lib/motion/frame-rate.ts` (new)
- `frontend/src/lib/motion/__tests__/frame-rate.test.ts` (new)
- `frontend/src/components/shared/hero-scene.tsx`

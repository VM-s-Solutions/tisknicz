---
id: T-0155
title: Hero 3D scene — pause the render loop off-screen + low-power GPU hint (dopady §4.9)
status: in_review
size: S
owner: frontend
created: 2026-07-23
updated: 2026-07-23
depends_on: []
blocks: []
user_stories: []
adrs: []
phase: 7
manual_steps: []
security_touching: false
layers: [frontend, optimizer]
---

# T-0155 — Hero 3D scene render gate

## Context

The 2026-07-04 dev-web review flagged the landing-page 3D hero
(dopady §4 🟡 9): WebGL console warnings ("GPU stall due to ReadPixels",
deprecated `THREE.Clock`) and jank risk on weaker devices, suggesting a
static fallback / `frameloop='demand'`. The load-time gating already
existed pre-review (`hero-scene-wrapper.tsx`: desktop-≥1024px only,
`prefers-reduced-motion`, `saveData`, <4-core skip, idle-callback mount) —
but once mounted, the Canvas animated at 60 fps for the whole visit, even
with the hero scrolled far out of view, which on a landing page is most of
the session.

## Scope

- `IntersectionObserver` on the scene container toggles the R3F Canvas
  `frameloop` between `always` (hero in viewport) and `never`
  (off-screen — last composited frame freezes, GPU cost drops to zero).
  Animations key off absolute elapsed time (sin-of-t drifts,
  next-meteor-at timestamps), so the time jump on resume is visually
  harmless.
- `powerPreference: 'low-power'` on the GL context — an ambient
  background belongs on the integrated GPU of dual-GPU machines; the
  scene is a handful of line/point primitives well within iGPU budget.

## Investigated, not actionable in our code

- **`THREE.Clock` → `THREE.Timer` deprecation warning** — emitted by
  @react-three/fiber's internal clock, not by this codebase (`state.clock`
  is R3F-provided). Goes away with a future fiber upgrade; a lib bump is
  not worth the regression surface for a console warning.
- **"GPU stall due to ReadPixels"** — nothing in the scene calls
  `readPixels`/`toDataURL`/`getImageData` on the WebGL canvas (the two
  `CanvasTexture`s are plain 2D canvases). The warning most plausibly came
  from the review session's DevTools/screenshot tooling; unreproducible
  from this code. Re-check during the T-0153 walk; if it reproduces
  without DevTools, file a follow-up with a capture.

## Acceptance criteria

- **AC-1** Given the landing page scrolled below the hero, when the scene
  container leaves the viewport, then the Canvas render loop stops (no
  rAF-driven GPU work) and resumes when scrolled back.
- **AC-2** Given the existing wrapper gating (mobile, reduced-motion,
  saveData, low-core), when those conditions hold, then behavior is
  unchanged (scene never mounts at all).
- **AC-3** Given the scene resumes after a pause, when animations
  continue, then no visual glitch beyond the designed time-jump occurs
  (meteor scheduling self-corrects; drifts are pure functions of t).

## Test plan reference

`tsc`, eslint, vitest suite (64/64 — jsdom has no WebGL; the 3D scene is
deliberately untested, consistent with its existing coverage), `next build`.
Manual: DevTools Performance on the landing page — GPU/rAF activity ceases
when the hero is scrolled out (fold into the T-0153 walk evidence).

## Status log

- 2026-07-23 `draft → in_progress → in_review` — closes the last unticketed
  dopady §4 finding; PR left open for operator merge.

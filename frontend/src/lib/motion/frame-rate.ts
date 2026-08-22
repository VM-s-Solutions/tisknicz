/**
 * Frame-rate–independent exponential smoothing.
 *
 * A per-frame lerp written as `value += (target - value) * 0.03` is only
 * correct at the frame rate it was tuned on. Measured 2026-08-22 on this
 * ProMotion display against the deployed dev site: **Chrome renders at
 * 121 Hz, Safari at 61 Hz** — neither drops frames (0 % over 200+ samples),
 * Safari simply runs at half the rate. So the same constant converges with a
 * ~0.28 s time constant in Chrome and ~0.55 s in Safari, and the hero camera
 * visibly trailed the pointer in Safari while feeling instant in Chrome.
 * That is the "laguje jen na Safari" report, in code.
 *
 * `smoothingFactor` rescales a constant that was authored for 60 fps to
 * whatever the current frame actually took, so motion looks identical at
 * 60, 120 or 144 Hz.
 *
 * @param perFrameAt60 the original constant, as authored for 60 fps (0..1)
 * @param delta seconds since the previous frame (three.js `useFrame` delta)
 */
export function smoothingFactor(perFrameAt60: number, delta: number): number {
  // A tab that was backgrounded or a GC pause hands back a huge delta; without
  // the clamp the factor saturates at 1 and the camera teleports.
  const clamped = Math.min(Math.max(delta, 0), 0.1);
  return 1 - Math.pow(1 - perFrameAt60, clamped * 60);
}

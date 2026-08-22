import { smoothingFactor } from '../frame-rate';

/**
 * The hero camera's `* 0.03` lerp was frame-rate dependent. Measured
 * 2026-08-22 against the deployed dev site on this ProMotion display:
 * Chrome renders at 121 Hz, Safari at 61 Hz, and NEITHER drops frames —
 * so Safari was not janky, it simply applied the constant half as often
 * and the camera trailed the pointer. That asymmetry is the bug.
 */
describe('smoothingFactor', () => {
  const PER_FRAME = 0.03;

  it('is the identity at exactly 60 fps — the rate the constant was authored for', () => {
    expect(smoothingFactor(PER_FRAME, 1 / 60)).toBeCloseTo(PER_FRAME, 10);
  });

  it('converges identically at 60 Hz and 120 Hz — the Safari/Chrome asymmetry is gone', () => {
    const step = (v: number, dt: number) => v + (1 - v) * smoothingFactor(PER_FRAME, dt);
    const at60 = step(0, 1 / 60);
    const at120 = step(step(0, 1 / 120), 1 / 120);
    expect(at120).toBeCloseTo(at60, 10);
  });

  it('reaches the same place after one second whatever the frame rate', () => {
    const settle = (hz: number) => {
      let v = 0;
      for (let i = 0; i < hz; i += 1) v += (1 - v) * smoothingFactor(PER_FRAME, 1 / hz);
      return v;
    };
    const baseline = settle(60);
    for (const hz of [30, 90, 120, 144]) {
      expect({ hz, settled: +settle(hz).toFixed(6) }).toEqual({ hz, settled: +baseline.toFixed(6) });
    }
  });

  it('clamps a huge delta so a backgrounded tab resumes without teleporting', () => {
    expect(smoothingFactor(PER_FRAME, 4)).toBeCloseTo(smoothingFactor(PER_FRAME, 0.1), 10);
    expect(smoothingFactor(PER_FRAME, 4)).toBeLessThan(1);
  });

  it('never returns a factor outside [0, 1]', () => {
    for (const dt of [0, -1, 0.001, 1 / 240, 1 / 60, 10]) {
      const f = smoothingFactor(PER_FRAME, dt);
      expect({ dt, inRange: f >= 0 && f <= 1 }).toEqual({ dt, inRange: true });
    }
  });
});

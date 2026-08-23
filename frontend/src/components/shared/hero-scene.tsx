'use client';

import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import * as THREE from 'three';
import { smoothingFactor } from '@/lib/motion/frame-rate';
import {
  readResolvedTheme,
  readServerResolvedTheme,
  subscribeToResolvedTheme,
} from '@/lib/theme/theme-store';

// Approved hero design (2026-07-06): zig-zag wireframe (3,4) torus knot facing
// the viewer like the Makables logo, spinning right around a vertical axis
// ((3,4) is invariant under a 180deg turn, so front and back views match),
// gently breathing, with an Interstellar-style black hole in its center:
// lensed glow ring, flat accretion disk, and light motes spiralling in.
// The values mirror the interactive design widget the scene was tuned in.
const KNOT_P = 3;
const KNOT_Q = 4;
const TUBE_RADIUS = 0.5;
const SPREAD = 1.15;
const SCALE = 1.5;
const MESH_DENSITY = 1.6;
const SPIN_RATE = 0.4 * 0.35;
const BREATHE_AMPLITUDE = 0.035;
const HOLE_RADIUS = 0.36;
const HOLE_BRIGHTNESS = 1;
const HOLE_WARM_MIX = 0.8;
const ABSORB_RATE = 0.7;
const SCENE_CENTER: [number, number, number] = [0.25, 0.2, -1.2];

// ---------------------------------------------------------------------------
// Theme (T-0191b)
//
// The scene was authored for a dark sky, and every luminous layer in it —
// stars, accretion motes, the lensing corona, the meteors — used
// AdditiveBlending. Additive *adds* light to what is already on the canvas,
// so over a light background it saturates to the background and the whole
// scene disappears. A light hero is therefore not a background-colour change:
// each of those layers has to swap to NormalBlending and draw DARK, which is
// the same trick photographers call a negative.
//
// Two consequences worth knowing before editing:
//
//   * Under Additive, a vertex colour is *brightness* — 0 means invisible.
//     Under Normal it is the literal colour, so 0 would mean solid black.
//     The light palette therefore interpolates from the page colour (which
//     is invisible against the page) toward an ink colour, which is why
//     `luminous()` takes the same 0..1 intensity in both themes and each
//     palette decides what that intensity means.
//   * Colours come from the CSS tokens rather than being duplicated here, so
//     a palette edit in globals.css moves the scene with it. The fog colour
//     especially: receding geometry fades to it, and any mismatch with the
//     real page background shows as a halo around the canvas.
// ---------------------------------------------------------------------------
type Rgb = readonly [number, number, number];

type ScenePalette = {
  readonly mode: 'light' | 'dark';
  readonly fog: string;
  readonly knot: string;
  readonly knotOpacity: number;
  /** Event horizon — a solid disc, the one layer that reads on any ground. */
  readonly hole: string;
  readonly blending: THREE.Blending;
  /** Corona halo / lensed ring, baked into the glow sprite texture. */
  readonly coronaHalo: Rgb;
  readonly coronaRing: Rgb;
  readonly coronaHaloToward: Rgb;
  readonly coronaRingToward: Rgb;
  /** Writes `intensity` (0..1) as this theme's colour into a vertex buffer. */
  readonly luminous: (
    target: Float32Array,
    offset: number,
    intensity: number,
    tint: LuminousTint
  ) => void;
};

/** Which of the palette's luminous colours a layer draws with. */
type LuminousTint = 'star' | 'mote' | 'meteor';

/**
 * `new THREE.Color(hex)` converts sRGB to the linear working space when
 * ColorManagement is on (r3f enables it). Raw floats written into a colour
 * BufferAttribute are NOT converted, so they must already be linear — hence
 * the round trip instead of parsing the hex by hand.
 */
function linearRgb(hex: string): Rgb {
  const color = new THREE.Color(hex);
  return [color.r, color.g, color.b];
}

function cssColor(name: string, fallback: string): string {
  if (typeof document === 'undefined') return fallback;
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

function buildPalette(mode: 'light' | 'dark'): ScenePalette {
  const fog = cssColor('--surface-primary', mode === 'light' ? '#f5f5f7' : '#0b1417');

  if (mode === 'dark') {
    // Byte-for-byte the approved 2026-07-06 scene. The literals are the
    // authored values, deliberately not tokens: this branch must not move
    // if the ink ramp is ever retuned.
    return {
      mode,
      fog,
      knot: '#14b8a6',
      knotOpacity: 0.6,
      hole: '#010102',
      blending: THREE.AdditiveBlending,
      coronaHalo: [45, 212, 191],
      coronaRing: [204, 251, 241],
      coronaHaloToward: CORONA_HALO_TOWARD_DARK,
      coronaRingToward: CORONA_RING_TOWARD_DARK,
      luminous: (target, offset, intensity, tint) => {
        // Additive: the colour IS the brightness, and the faint warm cast
        // (r < g, b just under g) is what keeps the motes from reading as a
        // flat cyan. Meteors carry their own slightly cooler cast.
        const warm = tint === 'meteor' ? 0.97 : 0.95;
        target[offset] = intensity * (tint === 'meteor' ? 0.9 : 0.82);
        target[offset + 1] = intensity;
        target[offset + 2] = intensity * warm;
      },
    };
  }

  // Light: draw dark, and let intensity fade each mark back into the page.
  const page = linearRgb(fog);
  // ink-500, not ink-600: a starfield reads as faint specks against a dark
  // sky at almost any value, but the same specks on a near-white page fall
  // under the eye's threshold entirely. Measured on the light hero, ink-600
  // was invisible at 1360px.
  const star = linearRgb(cssColor('--ink-500', '#64646c'));
  const mote = linearRgb(cssColor('--brand-400', '#0d6b62'));
  const meteor = linearRgb(cssColor('--ink-500', '#64646c'));

  return {
    mode,
    fog,
    knot: cssColor('--brand-500', '#0f7d72'),
    // A hair more opaque than the dark theme: a dark hairline on a light
    // ground carries less weight than a glowing one on a dark ground.
    knotOpacity: 0.66,
    hole: cssColor('--ink-50', '#111114'),
    blending: THREE.NormalBlending,
    // Sampled from the light brand steps: the ring is the darkest, so the
    // corona still reads as a rim rather than a smudge.
    coronaHalo: srgb255(cssColor('--brand-400', '#0d6b62')),
    coronaRing: srgb255(cssColor('--brand-200', '#0a4a45')),
    coronaHaloToward: srgb255(fog),
    coronaRingToward: srgb255(fog),
    luminous: (target, offset, intensity, tint) => {
      const ink = tint === 'star' ? star : tint === 'mote' ? mote : meteor;
      target[offset] = page[0] + (ink[0] - page[0]) * intensity;
      target[offset + 1] = page[1] + (ink[1] - page[1]) * intensity;
      target[offset + 2] = page[2] + (ink[2] - page[2]) * intensity;
    },
  };
}

function useScenePalette(): ScenePalette {
  const mode = useSyncExternalStore(
    subscribeToResolvedTheme,
    readResolvedTheme,
    readServerResolvedTheme
  );
  return useMemo(() => buildPalette(mode), [mode]);
}

// Authored at 60 fps. Do NOT apply it raw — see smoothingFactor.
const CAMERA_FOLLOW_PER_60HZ_FRAME = 0.03;

function CameraRig() {
  useFrame((state, delta) => {
    const targetX = state.pointer.x * 0.45;
    const targetY = state.pointer.y * 0.28;
    // Rescale the 60 fps constant to this frame's actual duration. Applied
    // raw it is frame-rate dependent: measured on a ProMotion display against
    // the deployed dev site, Chrome runs this scene at 121 Hz and Safari at
    // 61 Hz (neither drops frames), so the camera converged on the pointer
    // twice as slowly in Safari and read as lag that Chrome never showed.
    const follow = smoothingFactor(CAMERA_FOLLOW_PER_60HZ_FRAME, delta);
    state.camera.position.x += (targetX - state.camera.position.x) * follow;
    state.camera.position.y += (targetY - state.camera.position.y) * follow;
    state.camera.lookAt(0, 0, 0);
  });

  return null;
}

function WireKnot({ palette }: { palette: ScenePalette }) {
  const spinRef = useRef<THREE.Group>(null);

  const geometry = useMemo(() => {
    const norm = 1 / ((2 + 1 + TUBE_RADIUS) * 1.02);
    const maxExtent = (3 * SPREAD + TUBE_RADIUS) * norm;
    const fit = (2.41 * SCALE) / maxExtent;
    const tubularSegments = Math.round(140 * MESH_DENSITY);
    const radialSegments = Math.min(
      24,
      Math.max(6, Math.round(50 * TUBE_RADIUS * MESH_DENSITY))
    );
    const solid = new THREE.TorusKnotGeometry(
      2 * norm * SPREAD * fit,
      TUBE_RADIUS * norm * fit,
      tubularSegments,
      radialSegments,
      KNOT_P,
      KNOT_Q
    );
    const wire = new THREE.WireframeGeometry(solid);
    solid.dispose();
    return wire;
  }, []);

  useEffect(() => () => geometry.dispose(), [geometry]);

  useFrame((state) => {
    if (!spinRef.current) return;
    const t = state.clock.elapsedTime * SPIN_RATE;
    spinRef.current.rotation.y = t;
    spinRef.current.scale.setScalar(1 + BREATHE_AMPLITUDE * Math.sin(t * 2.4));
  });

  return (
    <group position={SCENE_CENTER}>
      <group ref={spinRef}>
        <lineSegments geometry={geometry}>
          <lineBasicMaterial
            color={palette.knot}
            transparent
            opacity={palette.knotOpacity}
          />
        </lineSegments>
      </group>
    </group>
  );
}

// Deterministic mulberry32 PRNG so useMemo bodies stay pure (no Math.random —
// violates react-hooks/purity) and the sky renders identically on every mount.
function createPrng(seed: number): () => number {
  let state = seed;
  return () => {
    state |= 0;
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function createStarTexture(): THREE.CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = 64;
  canvas.height = 64;
  const ctx = canvas.getContext('2d');
  if (ctx) {
    const gradient = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
    gradient.addColorStop(0, 'rgba(255,255,255,1)');
    gradient.addColorStop(0.35, 'rgba(255,255,255,0.55)');
    gradient.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, 64, 64);
  }
  return new THREE.CanvasTexture(canvas);
}

// The authored corona is a teal blended toward warm white by HOLE_WARM_MIX.
// On the dark theme that warm end is the glow; on the light theme the same
// blend runs toward the PAGE instead, so the corona fades out at its edges
// rather than lighting them up. One function, two destinations.
function coronaBlend(base: Rgb, toward: Rgb, alpha: number): string {
  const r = Math.round(base[0] + (toward[0] - base[0]) * HOLE_WARM_MIX);
  const g = Math.round(base[1] + (toward[1] - base[1]) * HOLE_WARM_MIX);
  const b = Math.round(base[2] + (toward[2] - base[2]) * HOLE_WARM_MIX);
  return `rgba(${r},${g},${b},${alpha})`;
}

/**
 * What the corona blends TOWARD at its edges. On dark that is the authored
 * warm white — the glow itself. On light it is the page colour, taken from
 * the palette rather than repeated here, so the halo fades out instead of
 * lighting up.
 */
const CORONA_HALO_TOWARD_DARK: Rgb = [255, 234, 210];
const CORONA_RING_TOWARD_DARK: Rgb = [255, 248, 235];

function srgb255(hex: string): Rgb {
  const value = hex.replace('#', '');
  const full = value.length === 3 ? value.split('').map((c) => c + c).join('') : value;
  return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16)) as unknown as Rgb;
}

// Halo, lensed ring, and photon rings in one billboard texture. 256px half
// width maps to 3.2x the hole radius, so the hole rim sits at 80px.
function createHoleGlowTexture(palette: ScenePalette): THREE.CanvasTexture {
  const holeColor = (alpha: number) =>
    coronaBlend(palette.coronaHalo, palette.coronaHaloToward, alpha);
  const holeColorBright = (alpha: number) =>
    coronaBlend(palette.coronaRing, palette.coronaRingToward, alpha);

  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 512;
  const ctx = canvas.getContext('2d');
  if (ctx) {
    const rim = 80;
    const b = HOLE_BRIGHTNESS;
    const halo = ctx.createRadialGradient(256, 256, rim, 256, 256, 256);
    halo.addColorStop(0, holeColor(0.28 * b));
    halo.addColorStop(0.5, holeColor(0.08 * b));
    halo.addColorStop(1, holeColor(0));
    ctx.fillStyle = halo;
    ctx.fillRect(0, 0, 512, 512);

    const lens = ctx.createRadialGradient(
      256,
      256,
      rim * 1.05,
      256,
      256,
      rim * 1.75
    );
    lens.addColorStop(0, holeColorBright(0.55 * b));
    lens.addColorStop(0.45, holeColor(0.22 * b));
    lens.addColorStop(1, holeColor(0));
    ctx.fillStyle = lens;
    ctx.beginPath();
    ctx.arc(256, 256, rim * 1.75, 0, Math.PI * 2);
    ctx.arc(256, 256, rim * 1.02, 0, Math.PI * 2, true);
    ctx.fill();

    ctx.strokeStyle = holeColorBright(Math.min(1, 0.95 * b));
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.arc(256, 256, rim * 1.02, 0, Math.PI * 2);
    ctx.stroke();
    ctx.strokeStyle = holeColor(0.35 * b);
    ctx.lineWidth = 8;
    ctx.beginPath();
    ctx.arc(256, 256, rim * 1.09, 0, Math.PI * 2);
    ctx.stroke();

    if (palette.mode === 'light') {
      // The halo gradient starts at `rim`, so everything inside it is filled
      // with the flat 28% stop. Additively over a black sphere that is
      // invisible; with NormalBlending it paints a translucent teal veil
      // straight across the event horizon and the black hole turns into a
      // grey-green smudge. Punch the interior out so the sphere shows
      // through — stopping just short of `rim` keeps the bright lensed ring
      // at 1.02 intact. Dark is left untouched: it must stay byte-identical
      // to the approved scene.
      ctx.globalCompositeOperation = 'destination-out';
      ctx.beginPath();
      ctx.arc(256, 256, rim * 0.99, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalCompositeOperation = 'source-over';
    }
  }
  return new THREE.CanvasTexture(canvas);
}

const MOTE_COUNT = 24;

type Mote = {
  angle: number;
  radius: number;
  delay: number;
};

function BlackHole({ palette }: { palette: ScenePalette }) {
  const glowRef = useRef<THREE.Sprite>(null);
  const positionAttrRef = useRef<THREE.BufferAttribute>(null);
  const colorAttrRef = useRef<THREE.BufferAttribute>(null);

  const buffers = useMemo(
    () => ({
      positions: new Float32Array(MOTE_COUNT * 3),
      colors: new Float32Array(MOTE_COUNT * 3),
    }),
    []
  );

  const motes = useRef<Mote[] | null>(null);

  const glowTexture = useMemo(() => createHoleGlowTexture(palette), [palette]);
  const moteTexture = useMemo(() => createStarTexture(), []);

  useEffect(
    () => () => {
      glowTexture.dispose();
      moteTexture.dispose();
    },
    [glowTexture, moteTexture]
  );

  useFrame((state, delta) => {
    const t = state.clock.elapsedTime * SPIN_RATE;
    const dt = Math.min(delta, 0.05);
    const pulse = 0.8 + 0.2 * Math.sin(t * 2.2);

    if (glowRef.current) glowRef.current.material.opacity = pulse;

    if (!motes.current) {
      motes.current = Array.from({ length: MOTE_COUNT }, () => ({
        angle: Math.random() * Math.PI * 2,
        radius: HOLE_RADIUS * (2.6 + Math.random() * 2.2),
        delay: Math.random() * 4,
      }));
    }

    const posAttr = positionAttrRef.current;
    const colorAttr = colorAttrRef.current;
    if (!posAttr || !colorAttr) return;
    const pos = posAttr.array as Float32Array;
    const col = colorAttr.array as Float32Array;

    for (let i = 0; i < MOTE_COUNT; i++) {
      const mote = motes.current[i];
      let brightness = 0;

      if (mote.delay > 0) {
        mote.delay -= dt * ABSORB_RATE;
      } else {
        const pull = 0.2 + 1.2 * Math.pow(HOLE_RADIUS / mote.radius, 2);
        mote.radius -= dt * pull * HOLE_RADIUS * 2.6;
        mote.angle += dt * (1.6 * HOLE_RADIUS / mote.radius) * 3.4;
        if (mote.radius <= HOLE_RADIUS * 1.04) {
          mote.angle = Math.random() * Math.PI * 2;
          mote.radius = HOLE_RADIUS * (2.6 + Math.random() * 2.2);
          mote.delay = Math.random() * 2;
        } else {
          const closeness = Math.min(
            1,
            1 - (mote.radius - HOLE_RADIUS) / (HOLE_RADIUS * 3.4)
          );
          brightness = 0.2 + 0.8 * closeness;
        }
      }

      pos[i * 3] = Math.cos(mote.angle) * mote.radius;
      pos[i * 3 + 1] = Math.sin(mote.angle) * mote.radius * 0.32;
      pos[i * 3 + 2] = 0;
      palette.luminous(col, i * 3, brightness, 'mote');
    }
    posAttr.needsUpdate = true;
    colorAttr.needsUpdate = true;
  });

  return (
    <group position={SCENE_CENTER}>
      <mesh>
        <sphereGeometry args={[HOLE_RADIUS, 32, 16]} />
        {/* Every other layer already opts out of fog; the event horizon has
            to as well on light. The scene sits ~9.5 units out with fog
            running 6..13.5, so the sphere was being blended ~47% toward the
            fog colour — invisible on dark (fog #0b1417 vs sphere #010102),
            but on light it washed the black hole into a grey-green smudge.
            Kept ON for dark so the approved scene stays byte-identical. */}
        <meshBasicMaterial color={palette.hole} fog={palette.mode === 'dark'} />
      </mesh>
      <sprite
        ref={glowRef}
        scale={[HOLE_RADIUS * 6.4, HOLE_RADIUS * 6.4, 1]}
      >
        <spriteMaterial
          map={glowTexture}
          transparent
          depthWrite={false}
          blending={palette.blending}
          fog={false}
        />
      </sprite>
      <points>
        <bufferGeometry>
          <bufferAttribute
            ref={positionAttrRef}
            attach="attributes-position"
            args={[buffers.positions, 3]}
          />
          <bufferAttribute
            ref={colorAttrRef}
            attach="attributes-color"
            args={[buffers.colors, 3]}
          />
        </bufferGeometry>
        <pointsMaterial
          size={0.08}
          map={moteTexture}
          vertexColors
          transparent
          depthWrite={false}
          blending={palette.blending}
          sizeAttenuation
          fog={false}
        />
      </points>
    </group>
  );
}

type StarsProps = {
  count: number;
  size: number;
  seed: number;
  palette: ScenePalette;
};

function Stars({ count, size, seed, palette }: StarsProps) {
  const colorAttrRef = useRef<THREE.BufferAttribute>(null);

  const sky = useMemo(() => {
    const rnd = createPrng(seed);
    const positions = new Float32Array(count * 3);
    const colors = new Float32Array(count * 3);
    const phases = new Float32Array(count);
    const speeds = new Float32Array(count);
    const baseGlow = new Float32Array(count);
    for (let i = 0; i < count; i++) {
      positions[i * 3] = (rnd() - 0.5) * 22;
      positions[i * 3 + 1] = (rnd() - 0.5) * 13;
      positions[i * 3 + 2] = -4 - rnd() * 5;
      phases[i] = rnd() * Math.PI * 2;
      speeds[i] = 0.5 + rnd() * 1.5;
      baseGlow[i] = 0.45 + rnd() * 0.55;
    }
    return { positions, colors, phases, speeds, baseGlow };
  }, [count, seed]);

  const texture = useMemo(() => createStarTexture(), []);

  useEffect(() => () => texture.dispose(), [texture]);

  useFrame((state) => {
    const attr = colorAttrRef.current;
    if (!attr) return;
    const t = state.clock.elapsedTime;
    const arr = attr.array as Float32Array;
    for (let i = 0; i < count; i++) {
      const glow =
        sky.baseGlow[i] *
        (0.72 + 0.28 * Math.sin(t * sky.speeds[i] + sky.phases[i]));
      palette.luminous(arr, i * 3, glow, 'star');
    }
    attr.needsUpdate = true;
  });

  return (
    <points>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[sky.positions, 3]} />
        <bufferAttribute
          ref={colorAttrRef}
          attach="attributes-color"
          args={[sky.colors, 3]}
        />
      </bufferGeometry>
      <pointsMaterial
        size={size}
        map={texture}
        vertexColors
        transparent
        depthWrite={false}
        blending={palette.blending}
        sizeAttenuation
        fog={false}
      />
    </points>
  );
}

const METEOR_TRAIL_POINTS = 12;
const METEOR_TRAIL_LENGTH = 2.2;

function ShootingStar({
  initialDelay,
  palette,
}: {
  initialDelay: number;
  palette: ScenePalette;
}) {
  const lineRef = useRef<THREE.Line>(null);
  const flight = useRef({
    active: false,
    nextAt: initialDelay,
    startedAt: 0,
    duration: 1,
    distance: 5,
    start: new THREE.Vector3(),
    direction: new THREE.Vector3(),
  });

  const meteor = useMemo(() => {
    const positions = new Float32Array(METEOR_TRAIL_POINTS * 3);
    const colors = new Float32Array(METEOR_TRAIL_POINTS * 3);
    for (let i = 0; i < METEOR_TRAIL_POINTS; i++) {
      // Head to tail. On dark the tail dims to nothing; on light it fades
      // back into the page, which is the same thing seen from the other side.
      const fade = 1 - i / (METEOR_TRAIL_POINTS - 1);
      palette.luminous(colors, i * 3, fade, 'meteor');
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    const material = new THREE.LineBasicMaterial({
      vertexColors: true,
      transparent: true,
      opacity: 0,
      depthWrite: false,
      blending: palette.blending,
      fog: false,
    });
    const line = new THREE.Line(geometry, material);
    line.frustumCulled = false;
    return line;
  }, [palette]);

  useEffect(
    () => () => {
      meteor.geometry.dispose();
      (meteor.material as THREE.LineBasicMaterial).dispose();
    },
    [meteor]
  );

  useFrame((state) => {
    const line = lineRef.current;
    if (!line) return;
    const material = line.material as THREE.LineBasicMaterial;
    const positionAttr = line.geometry.getAttribute(
      'position'
    ) as THREE.BufferAttribute;
    const t = state.clock.elapsedTime;
    const s = flight.current;

    if (!s.active) {
      if (t < s.nextAt) return;
      s.active = true;
      s.startedAt = t;
      s.duration = 0.8 + Math.random() * 0.7;
      s.distance = 4 + Math.random() * 3;
      s.start.set(
        (Math.random() - 0.5) * 14,
        2 + Math.random() * 3.5,
        -4 - Math.random() * 3
      );
      const angle = ((35 + Math.random() * 25) * Math.PI) / 180;
      const side = Math.random() < 0.5 ? -1 : 1;
      s.direction.set(Math.cos(angle) * side, -Math.sin(angle), 0);
    }

    const progress = (t - s.startedAt) / s.duration;
    if (progress >= 1) {
      s.active = false;
      s.nextAt = t + 4 + Math.random() * 8;
      material.opacity = 0;
      return;
    }

    // Real meteors flare up fast and burn out gradually.
    const brightness =
      progress < 0.18
        ? progress / 0.18
        : Math.pow(1 - (progress - 0.18) / 0.82, 1.6);

    const arr = positionAttr.array as Float32Array;
    for (let i = 0; i < METEOR_TRAIL_POINTS; i++) {
      const back = (METEOR_TRAIL_LENGTH * i) / (METEOR_TRAIL_POINTS - 1);
      const along = progress * s.distance - back;
      arr[i * 3] = s.start.x + s.direction.x * along;
      arr[i * 3 + 1] = s.start.y + s.direction.y * along;
      arr[i * 3 + 2] = s.start.z;
    }
    positionAttr.needsUpdate = true;
    material.opacity = brightness * 0.9;
  });

  return <primitive ref={lineRef} object={meteor} />;
}

export function HeroScene() {
  const containerRef = useRef<HTMLDivElement>(null);
  const palette = useScenePalette();
  // Render-loop gate (T-0155, dopady §4.9): a decorative background must
  // not burn GPU at 60 fps while the hero is scrolled out of view — on
  // the landing page that is most of the visit. `frameloop='never'`
  // freezes the last composited frame (the canvas stays as-is, which is
  // fine off-screen) and the loop resumes the moment the hero re-enters
  // the viewport. Animations key off absolute elapsed time (sin(t) drifts,
  // next-meteor-at timestamps), so the time jump on resume is harmless.
  const [frameloop, setFrameloop] = useState<'always' | 'never'>('always');

  useEffect(() => {
    const container = containerRef.current;
    if (!container || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver(
      ([entry]) => setFrameloop(entry.isIntersecting ? 'always' : 'never'),
      { threshold: 0 },
    );
    observer.observe(container);
    return () => observer.disconnect();
  }, []);

  return (
    <div ref={containerRef} className="absolute inset-0 z-0">
      <Canvas
        camera={{ position: [0, 0, 8.3], fov: 44 }}
        dpr={[1, 1.5]}
        frameloop={frameloop}
        // 'low-power' nudges dual-GPU machines to the integrated GPU —
        // right for an ambient background; the scene is a handful of
        // line/point primitives and comfortably within iGPU budget.
        gl={{ antialias: true, alpha: true, powerPreference: 'low-power' }}
        style={{ background: 'transparent' }}
      >
        <CameraRig />
        {/* Read from --surface-primary rather than hardcoded: receding
            geometry fades to this colour, so any mismatch with the real page
            background shows as a halo around the scene. */}
        <fog attach="fog" args={[palette.fog, 6, 13.5]} />

        {/* Keyed on the theme so a swap rebuilds the materials and the baked
            corona texture. Only the CONTENTS remount — the <Canvas> and its
            WebGL context are untouched, because contexts are a scarce
            per-tab resource and recreating one per toggle would eventually
            hit the browser's ~16 limit. */}
        <group key={palette.mode}>
          <WireKnot palette={palette} />
          <BlackHole palette={palette} />
          <Stars count={140} size={0.07} seed={0x9e3779b9} palette={palette} />
          <Stars count={35} size={0.14} seed={0x85ebca6b} palette={palette} />
          <ShootingStar initialDelay={3} palette={palette} />
          <ShootingStar initialDelay={9.5} palette={palette} />
        </group>
      </Canvas>
    </div>
  );
}

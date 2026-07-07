'use client';

import { useEffect, useMemo, useRef } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import * as THREE from 'three';

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

function CameraRig() {
  useFrame((state) => {
    const targetX = state.pointer.x * 0.45;
    const targetY = state.pointer.y * 0.28;
    state.camera.position.x += (targetX - state.camera.position.x) * 0.03;
    state.camera.position.y += (targetY - state.camera.position.y) * 0.03;
    state.camera.lookAt(0, 0, 0);
  });

  return null;
}

function WireKnot() {
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
          <lineBasicMaterial color="#14b8a6" transparent opacity={0.6} />
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

// Teal-to-white blends matching the approved design widget (mix 0..1).
function holeColor(alpha: number): string {
  const r = Math.round(45 + (255 - 45) * HOLE_WARM_MIX);
  const g = Math.round(212 + (234 - 212) * HOLE_WARM_MIX);
  const b = Math.round(191 + (210 - 191) * HOLE_WARM_MIX);
  return `rgba(${r},${g},${b},${alpha})`;
}

function holeColorBright(alpha: number): string {
  const r = Math.round(204 + (255 - 204) * HOLE_WARM_MIX);
  const g = Math.round(251 + (248 - 251) * HOLE_WARM_MIX);
  const b = Math.round(241 + (235 - 241) * HOLE_WARM_MIX);
  return `rgba(${r},${g},${b},${alpha})`;
}

// Halo, lensed ring, and photon rings in one billboard texture. 256px half
// width maps to 3.2x the hole radius, so the hole rim sits at 80px.
function createHoleGlowTexture(): THREE.CanvasTexture {
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
  }
  return new THREE.CanvasTexture(canvas);
}

const MOTE_COUNT = 24;

type Mote = {
  angle: number;
  radius: number;
  delay: number;
};

function BlackHole() {
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

  const glowTexture = useMemo(() => createHoleGlowTexture(), []);
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
      col[i * 3] = brightness * 0.9;
      col[i * 3 + 1] = brightness;
      col[i * 3 + 2] = brightness * 0.95;
    }
    posAttr.needsUpdate = true;
    colorAttr.needsUpdate = true;
  });

  return (
    <group position={SCENE_CENTER}>
      <mesh>
        <sphereGeometry args={[HOLE_RADIUS, 32, 16]} />
        <meshBasicMaterial color="#010102" />
      </mesh>
      <sprite
        ref={glowRef}
        scale={[HOLE_RADIUS * 6.4, HOLE_RADIUS * 6.4, 1]}
      >
        <spriteMaterial
          map={glowTexture}
          transparent
          depthWrite={false}
          blending={THREE.AdditiveBlending}
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
          blending={THREE.AdditiveBlending}
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
};

function Stars({ count, size, seed }: StarsProps) {
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
      arr[i * 3] = glow * 0.82;
      arr[i * 3 + 1] = glow;
      arr[i * 3 + 2] = glow * 0.95;
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
        blending={THREE.AdditiveBlending}
        sizeAttenuation
        fog={false}
      />
    </points>
  );
}

const METEOR_TRAIL_POINTS = 12;
const METEOR_TRAIL_LENGTH = 2.2;

function ShootingStar({ initialDelay }: { initialDelay: number }) {
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
      const fade = 1 - i / (METEOR_TRAIL_POINTS - 1);
      colors[i * 3] = fade * 0.9;
      colors[i * 3 + 1] = fade;
      colors[i * 3 + 2] = fade * 0.97;
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    const material = new THREE.LineBasicMaterial({
      vertexColors: true,
      transparent: true,
      opacity: 0,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
      fog: false,
    });
    const line = new THREE.Line(geometry, material);
    line.frustumCulled = false;
    return line;
  }, []);

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
  return (
    <div className="absolute inset-0 z-0">
      <Canvas
        camera={{ position: [0, 0, 8.3], fov: 44 }}
        dpr={[1, 1.5]}
        gl={{ antialias: true, alpha: true }}
        style={{ background: 'transparent' }}
      >
        <CameraRig />
        <fog attach="fog" args={['#09090b', 6, 13.5]} />

        <WireKnot />
        <BlackHole />
        <Stars count={140} size={0.07} seed={0x9e3779b9} />
        <Stars count={35} size={0.14} seed={0x85ebca6b} />
        <ShootingStar initialDelay={3} />
        <ShootingStar initialDelay={9.5} />
      </Canvas>
    </div>
  );
}

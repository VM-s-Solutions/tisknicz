'use client';

import { useEffect, useMemo, useRef } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import * as THREE from 'three';

// Approved hero design (2026-07-05): zig-zag wireframe torus knot facing the
// viewer, spinning right around a vertical axis tilted to the right.
// The values mirror the interactive design widget the knot was tuned in.
const KNOT_P = 2;
const KNOT_Q = 5;
const TUBE_RADIUS = 0.5;
const SPREAD = 1.15;
const SCALE = 1.5;
const MESH_DENSITY = 1.6;
const SPIN_SPEED = 0.3;
const AXIS_TILT_DEG = 23;

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
    spinRef.current.rotation.y = state.clock.elapsedTime * SPIN_SPEED * 0.35;
  });

  return (
    <group
      position={[0.25, 0.2, -1.2]}
      rotation={[0, 0, (-AXIS_TILT_DEG * Math.PI) / 180]}
    >
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
    const positionAttr = new THREE.BufferAttribute(positions, 3);
    geometry.setAttribute('position', positionAttr);
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
    return { line, geometry, material, positionAttr };
  }, []);

  useEffect(
    () => () => {
      meteor.geometry.dispose();
      meteor.material.dispose();
    },
    [meteor]
  );

  useFrame((state) => {
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
      meteor.material.opacity = 0;
      return;
    }

    // Real meteors flare up fast and burn out gradually.
    const brightness =
      progress < 0.18
        ? progress / 0.18
        : Math.pow(1 - (progress - 0.18) / 0.82, 1.6);

    const arr = meteor.positionAttr.array as Float32Array;
    for (let i = 0; i < METEOR_TRAIL_POINTS; i++) {
      const back = (METEOR_TRAIL_LENGTH * i) / (METEOR_TRAIL_POINTS - 1);
      const along = progress * s.distance - back;
      arr[i * 3] = s.start.x + s.direction.x * along;
      arr[i * 3 + 1] = s.start.y + s.direction.y * along;
      arr[i * 3 + 2] = s.start.z;
    }
    meteor.positionAttr.needsUpdate = true;
    meteor.material.opacity = brightness * 0.9;
  });

  return <primitive object={meteor.line} />;
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
        <Stars count={140} size={0.07} seed={0x9e3779b9} />
        <Stars count={35} size={0.14} seed={0x85ebca6b} />
        <ShootingStar initialDelay={3} />
        <ShootingStar initialDelay={9.5} />
      </Canvas>
    </div>
  );
}

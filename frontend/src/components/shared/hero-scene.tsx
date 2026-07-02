'use client';

import { useRef, useMemo } from 'react';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { Float } from '@react-three/drei';
import * as THREE from 'three';

function CameraRig() {
  const { camera, pointer } = useThree();

  useFrame(() => {
    const targetX = pointer.x * 0.45;
    const targetY = pointer.y * 0.28;
    camera.position.x += (targetX - camera.position.x) * 0.03;
    camera.position.y += (targetY - camera.position.y) * 0.03;
    camera.lookAt(0, 0, 0);
  });

  return null;
}

function CoreShell() {
  const wireRef = useRef<THREE.Mesh>(null);
  const coreRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!wireRef.current || !coreRef.current) return;
    wireRef.current.rotation.y = state.clock.elapsedTime * 0.22;
    wireRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.1) * 0.2;
    coreRef.current.rotation.y = -state.clock.elapsedTime * 0.12;
    coreRef.current.rotation.z = state.clock.elapsedTime * 0.08;
  });

  return (
    <Float speed={1.1} rotationIntensity={0.3} floatIntensity={0.6}>
      <group position={[0.25, 0.2, -1.2]}>
        <mesh ref={wireRef} scale={2.4}>
          <torusKnotGeometry args={[1, 0.26, 220, 36, 2, 5]} />
          <meshStandardMaterial
            color="#d4d4d8"
            emissive="#14b8a6"
            emissiveIntensity={0.8}
            metalness={0.95}
            roughness={0.1}
            transparent
            opacity={0.32}
            wireframe
          />
        </mesh>

        <mesh ref={coreRef} scale={1.05}>
          <icosahedronGeometry args={[0.74, 1]} />
          <meshStandardMaterial
            color="#14b8a6"
            emissive="#14b8a6"
            emissiveIntensity={0.55}
            metalness={0.55}
            roughness={0.25}
            transparent
            opacity={0.25}
          />
        </mesh>

        <mesh scale={2.8} rotation={[Math.PI / 3.2, 0, 0]}>
          <torusGeometry args={[1, 0.04, 16, 96]} />
          <meshStandardMaterial
            color="#2dd4bf"
            emissive="#2dd4bf"
            emissiveIntensity={0.9}
            transparent
            opacity={0.24}
            metalness={0.7}
            roughness={0.3}
          />
        </mesh>
      </group>
    </Float>
  );
}

function OrbitalFrames() {
  const ringsRef = useRef<THREE.Group>(null);

  useFrame((state) => {
    if (!ringsRef.current) return;
    ringsRef.current.rotation.y = state.clock.elapsedTime * 0.11;
    ringsRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.2) * 0.08;
  });

  return (
    <group ref={ringsRef} position={[0.6, 0.15, -1.8]}>
      <Float speed={0.8} rotationIntensity={0.1} floatIntensity={0.3}>
        <mesh rotation={[0.45, 0.2, 0.2]} scale={2.8}>
          <torusGeometry args={[1, 0.045, 20, 120]} />
          <meshStandardMaterial
            color="#99f6e4"
            emissive="#2dd4bf"
            emissiveIntensity={0.75}
            transparent
            opacity={0.24}
            metalness={0.5}
            roughness={0.35}
          />
        </mesh>
      </Float>

      <Float speed={0.9} rotationIntensity={0.12} floatIntensity={0.24}>
        <mesh rotation={[-0.25, 0.7, 1.1]} scale={3.25}>
          <torusGeometry args={[1, 0.03, 16, 96]} />
          <meshStandardMaterial
            color="#a1a1aa"
            emissive="#71717a"
            emissiveIntensity={0.55}
            transparent
            opacity={0.18}
            metalness={0.8}
            roughness={0.18}
          />
        </mesh>
      </Float>
    </group>
  );
}

function FloatingRing({ position, scale, speed }: { position: [number, number, number]; scale: number; speed: number }) {
  const meshRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.x = state.clock.elapsedTime * speed;
    meshRef.current.rotation.y = state.clock.elapsedTime * speed * 0.78;
  });

  return (
    <Float speed={1.2} rotationIntensity={0.3} floatIntensity={0.4}>
      <mesh ref={meshRef} position={position} scale={scale}>
        <torusGeometry args={[1, 0.06, 16, 48]} />
        <meshStandardMaterial
          color="#d4d4d8"
          emissive="#14b8a6"
          emissiveIntensity={1}
          metalness={0.6}
          roughness={0.2}
          transparent
          opacity={0.28}
        />
      </mesh>
    </Float>
  );
}

function Particles() {
  const pointsRef = useRef<THREE.Points>(null);

  // Deterministic mulberry32 PRNG seeded by the particle index, so the
  // useMemo body stays pure (no Math.random — violates react-hooks/purity)
  // and the hero scene renders identically on every mount, including SSR.
  const positions = useMemo(() => {
    const count = 110;
    const pos = new Float32Array(count * 3);
    let state = 0x9e3779b9;
    const next = () => {
      state |= 0;
      state = (state + 0x6d2b79f5) | 0;
      let t = Math.imul(state ^ (state >>> 15), 1 | state);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
    for (let i = 0; i < count; i++) {
      pos[i * 3] = (next() - 0.5) * 20;
      pos[i * 3 + 1] = (next() - 0.5) * 12;
      pos[i * 3 + 2] = (next() - 0.5) * 10 - 3;
    }
    return pos;
  }, []);

  useFrame((state) => {
    if (!pointsRef.current) return;
    pointsRef.current.rotation.y = state.clock.elapsedTime * 0.01;
    pointsRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.05) * 0.05;
  });

  return (
    <points ref={pointsRef}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} />
      </bufferGeometry>
      <pointsMaterial
        size={0.045}
        color="#99f6e4"
        transparent
        opacity={0.42}
        sizeAttenuation
      />
    </points>
  );
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
        <fog attach="fog" args={['#09090b', 7.8, 14.5]} />

        <ambientLight intensity={0.2} />
        <pointLight position={[-4.8, 4.1, 5]} intensity={1.75} color="#2dd4bf" distance={22} />
        <pointLight position={[5.4, -2.2, 4]} intensity={0.95} color="#d4d4d8" distance={20} />
        <spotLight position={[0, 5.2, 5]} angle={0.42} penumbra={0.72} intensity={1.05} color="#14b8a6" />

        <CoreShell />
        <OrbitalFrames />
        <FloatingRing position={[0.2, 0.1, -3.1]} scale={1.05} speed={0.1} />
        <Particles />
      </Canvas>
    </div>
  );
}

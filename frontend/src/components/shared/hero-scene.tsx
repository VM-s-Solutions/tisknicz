'use client';

import { useRef, useMemo } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import { Float } from '@react-three/drei';
import * as THREE from 'three';

function TorusKnotMesh() {
  const meshRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.y = state.clock.elapsedTime * 0.1;
    meshRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.06) * 0.12;
  });

  return (
    <Float speed={1} rotationIntensity={0.15} floatIntensity={0.5}>
      <mesh ref={meshRef} scale={2.2} position={[0, 0.3, -1]}>
        <torusKnotGeometry args={[1, 0.32, 128, 32, 2, 3]} />
        <meshStandardMaterial
          color="#a1a1aa"
          emissive="#71717a"
          emissiveIntensity={0.8}
          metalness={0.95}
          roughness={0.05}
          transparent
          opacity={0.25}
          wireframe
        />
      </mesh>
    </Float>
  );
}

function SolidTorusKnot() {
  const meshRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.y = state.clock.elapsedTime * 0.1;
    meshRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.06) * 0.12;
  });

  return (
    <Float speed={1} rotationIntensity={0.15} floatIntensity={0.5}>
      <mesh ref={meshRef} scale={1.8} position={[0, 0.3, -1]}>
        <torusKnotGeometry args={[1, 0.32, 128, 32, 2, 3]} />
        <meshStandardMaterial
          color="#52525b"
          emissive="#71717a"
          emissiveIntensity={0.4}
          metalness={0.9}
          roughness={0.1}
          transparent
          opacity={0.1}
        />
      </mesh>
    </Float>
  );
}

function PrintLayers() {
  const groupRef = useRef<THREE.Group>(null);

  useFrame((state) => {
    if (!groupRef.current) return;
    groupRef.current.rotation.y = state.clock.elapsedTime * 0.18;
  });

  return (
    <Float speed={1.2} rotationIntensity={0.1} floatIntensity={0.3}>
      <group ref={groupRef} position={[4.5, -1, -2]} scale={0.8}>
        {Array.from({ length: 8 }, (_, i) => (
          <mesh key={i} position={[0, i * 0.18 - 0.5, 0]}>
            <boxGeometry args={[1.2 - i * 0.04, 0.12, 1.2 - i * 0.04]} />
            <meshStandardMaterial
              color={`hsl(0, 0%, ${38 + i * 5}%)`}
              emissive={`hsl(0, 0%, ${25 + i * 3}%)`}
              emissiveIntensity={0.6}
              metalness={0.7}
              roughness={0.25}
              transparent
              opacity={0.5}
            />
          </mesh>
        ))}
      </group>
    </Float>
  );
}

function GearMesh() {
  const meshRef = useRef<THREE.Mesh>(null);

  const gearShape = useMemo(() => {
    const shape = new THREE.Shape();
    const teeth = 10;
    const innerR = 0.5;
    const outerR = 0.72;

    for (let i = 0; i < teeth; i++) {
      const a = (i / teeth) * Math.PI * 2;
      const na = ((i + 1) / teeth) * Math.PI * 2;
      const ts = a + 0.03;
      const te = a + (na - a) * 0.4;
      const vs = te + 0.03;
      const ve = na - 0.03;

      if (i === 0) {
        shape.moveTo(Math.cos(ts) * outerR, Math.sin(ts) * outerR);
      }
      shape.lineTo(Math.cos(te) * outerR, Math.sin(te) * outerR);
      shape.lineTo(Math.cos(vs) * innerR, Math.sin(vs) * innerR);
      shape.lineTo(Math.cos(ve) * innerR, Math.sin(ve) * innerR);
      shape.lineTo(Math.cos(na + 0.03) * outerR, Math.sin(na + 0.03) * outerR);
    }

    const hole = new THREE.Path();
    hole.absarc(0, 0, 0.18, 0, Math.PI * 2, false);
    shape.holes.push(hole);
    return shape;
  }, []);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.z = state.clock.elapsedTime * 0.2;
  });

  return (
    <Float speed={0.8} rotationIntensity={0.08} floatIntensity={0.25}>
      <mesh ref={meshRef} position={[-4, -0.5, -1.5]} scale={1.8}>
        <extrudeGeometry args={[gearShape, { depth: 0.2, bevelEnabled: true, bevelThickness: 0.03, bevelSize: 0.03, bevelSegments: 2 }]} />
        <meshStandardMaterial
          color="#a1a1aa"
          emissive="#71717a"
          emissiveIntensity={0.6}
          metalness={0.85}
          roughness={0.12}
          transparent
          opacity={0.35}
        />
      </mesh>
    </Float>
  );
}

function FloatingRing({ position, scale, speed }: { position: [number, number, number]; scale: number; speed: number }) {
  const meshRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.x = state.clock.elapsedTime * speed;
    meshRef.current.rotation.y = state.clock.elapsedTime * speed * 0.7;
  });

  return (
    <Float speed={1.2} rotationIntensity={0.3} floatIntensity={0.4}>
      <mesh ref={meshRef} position={position} scale={scale}>
        <torusGeometry args={[1, 0.06, 16, 48]} />
        <meshStandardMaterial
          color="#d4d4d8"
          emissive="#a1a1aa"
          emissiveIntensity={1}
          metalness={0.6}
          roughness={0.2}
          transparent
          opacity={0.3}
        />
      </mesh>
    </Float>
  );
}

function Particles() {
  const pointsRef = useRef<THREE.Points>(null);

  const positions = useMemo(() => {
    const count = 120;
    const pos = new Float32Array(count * 3);
    for (let i = 0; i < count; i++) {
      pos[i * 3] = (Math.random() - 0.5) * 16;
      pos[i * 3 + 1] = (Math.random() - 0.5) * 10;
      pos[i * 3 + 2] = (Math.random() - 0.5) * 6 - 2;
    }
    return pos;
  }, []);

  useFrame((state) => {
    if (!pointsRef.current) return;
    pointsRef.current.rotation.y = state.clock.elapsedTime * 0.012;
  });

  return (
    <points ref={pointsRef}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} />
      </bufferGeometry>
      <pointsMaterial
        size={0.05}
        color="#d4d4d8"
        transparent
        opacity={0.4}
        sizeAttenuation
      />
    </points>
  );
}

export function HeroScene() {
  return (
    <div className="absolute inset-0 z-0">
      <Canvas
        camera={{ position: [0, 0, 8], fov: 45 }}
        dpr={[1, 1.5]}
        gl={{ antialias: true, alpha: true }}
        style={{ background: 'transparent' }}
      >
        <ambientLight intensity={0.1} />
        <pointLight position={[-5, 4, 4]} intensity={1.5} color="#d4d4d8" distance={20} />
        <pointLight position={[5, -3, 3]} intensity={1} color="#a1a1aa" distance={18} />
        <pointLight position={[0, 2, 6]} intensity={0.6} color="#e4e4e7" distance={15} />

        <TorusKnotMesh />
        <SolidTorusKnot />
        <PrintLayers />
        <GearMesh />
        <FloatingRing position={[-2, 2, -3]} scale={1} speed={0.12} />
        <FloatingRing position={[3, 1.5, -2.5]} scale={0.7} speed={0.2} />
        <FloatingRing position={[0, -2, -4]} scale={1.3} speed={0.08} />
        <Particles />
      </Canvas>
    </div>
  );
}

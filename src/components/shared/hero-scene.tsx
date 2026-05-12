'use client';

import { useRef, useMemo } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import { Float } from '@react-three/drei';
import * as THREE from 'three';

function TorusKnotMesh() {
  const meshRef = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!meshRef.current) return;
    meshRef.current.rotation.y = state.clock.elapsedTime * 0.12;
    meshRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.08) * 0.15;
  });

  return (
    <Float speed={1.2} rotationIntensity={0.2} floatIntensity={0.6}>
      <mesh ref={meshRef} scale={2.2} position={[0, 0.3, -1]}>
        <torusKnotGeometry args={[1, 0.32, 128, 32, 2, 3]} />
        <meshStandardMaterial
          color="#2dd4bf"
          emissive="#14b8a6"
          emissiveIntensity={1.2}
          metalness={0.95}
          roughness={0.05}
          transparent
          opacity={0.35}
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
    meshRef.current.rotation.y = state.clock.elapsedTime * 0.12;
    meshRef.current.rotation.x = Math.sin(state.clock.elapsedTime * 0.08) * 0.15;
  });

  return (
    <Float speed={1.2} rotationIntensity={0.2} floatIntensity={0.6}>
      <mesh ref={meshRef} scale={1.8} position={[0, 0.3, -1]}>
        <torusKnotGeometry args={[1, 0.32, 128, 32, 2, 3]} />
        <meshStandardMaterial
          color="#0d9488"
          emissive="#14b8a6"
          emissiveIntensity={0.6}
          metalness={0.9}
          roughness={0.1}
          transparent
          opacity={0.15}
        />
      </mesh>
    </Float>
  );
}

function PrintLayers() {
  const groupRef = useRef<THREE.Group>(null);

  useFrame((state) => {
    if (!groupRef.current) return;
    groupRef.current.rotation.y = state.clock.elapsedTime * 0.2;
  });

  return (
    <Float speed={1.5} rotationIntensity={0.15} floatIntensity={0.4}>
      <group ref={groupRef} position={[4.5, -1, -2]} scale={0.8}>
        {Array.from({ length: 8 }, (_, i) => (
          <mesh key={i} position={[0, i * 0.18 - 0.5, 0]}>
            <boxGeometry args={[1.2 - i * 0.04, 0.12, 1.2 - i * 0.04]} />
            <meshStandardMaterial
              color={`hsl(${168 + i * 5}, 80%, ${40 + i * 4}%)`}
              emissive={`hsl(${168 + i * 5}, 70%, 25%)`}
              emissiveIntensity={1}
              metalness={0.6}
              roughness={0.3}
              transparent
              opacity={0.7}
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
    meshRef.current.rotation.z = state.clock.elapsedTime * 0.25;
  });

  return (
    <Float speed={1} rotationIntensity={0.1} floatIntensity={0.3}>
      <mesh ref={meshRef} position={[-4, -0.5, -1.5]} scale={1.8}>
        <extrudeGeometry args={[gearShape, { depth: 0.2, bevelEnabled: true, bevelThickness: 0.03, bevelSize: 0.03, bevelSegments: 2 }]} />
        <meshStandardMaterial
          color="#14b8a6"
          emissive="#0d9488"
          emissiveIntensity={1}
          metalness={0.8}
          roughness={0.15}
          transparent
          opacity={0.5}
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
    <Float speed={1.5} rotationIntensity={0.4} floatIntensity={0.5}>
      <mesh ref={meshRef} position={position} scale={scale}>
        <torusGeometry args={[1, 0.06, 16, 48]} />
        <meshStandardMaterial
          color="#5eead4"
          emissive="#2dd4bf"
          emissiveIntensity={1.5}
          metalness={0.5}
          roughness={0.2}
          transparent
          opacity={0.4}
        />
      </mesh>
    </Float>
  );
}

function Particles() {
  const pointsRef = useRef<THREE.Points>(null);

  const positions = useMemo(() => {
    const count = 150;
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
    pointsRef.current.rotation.y = state.clock.elapsedTime * 0.015;
  });

  return (
    <points ref={pointsRef}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} />
      </bufferGeometry>
      <pointsMaterial
        size={0.06}
        color="#5eead4"
        transparent
        opacity={0.6}
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
        <ambientLight intensity={0.15} />
        <pointLight position={[-5, 4, 4]} intensity={2} color="#2dd4bf" distance={20} />
        <pointLight position={[5, -3, 3]} intensity={1.5} color="#14b8a6" distance={18} />
        <pointLight position={[0, 2, 6]} intensity={0.8} color="#99f6e4" distance={15} />

        <TorusKnotMesh />
        <SolidTorusKnot />
        <PrintLayers />
        <GearMesh />
        <FloatingRing position={[-2, 2, -3]} scale={1} speed={0.15} />
        <FloatingRing position={[3, 1.5, -2.5]} scale={0.7} speed={0.25} />
        <FloatingRing position={[0, -2, -4]} scale={1.3} speed={0.1} />
        <Particles />
      </Canvas>
    </div>
  );
}

'use client';

import dynamic from 'next/dynamic';

const HeroScene = dynamic(
  () => import('@/components/shared/hero-scene').then((mod) => mod.HeroScene),
  { ssr: false }
);

export function HeroSceneWrapper() {
  return <HeroScene />;
}

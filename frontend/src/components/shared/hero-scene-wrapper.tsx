'use client';

import dynamic from 'next/dynamic';
import { useEffect, useState } from 'react';

const HeroScene = dynamic(
  () => import('@/components/shared/hero-scene').then((mod) => mod.HeroScene),
  { ssr: false }
);

function shouldLoadHeroScene(): boolean {
  if (typeof window === 'undefined') {
    return false;
  }

  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    return false;
  }

  if (!window.matchMedia('(min-width: 1024px)').matches) {
    return false;
  }

  const connection = (navigator as Navigator & {
    connection?: { saveData?: boolean };
  }).connection;
  if (connection?.saveData) {
    return false;
  }

  if (navigator.hardwareConcurrency > 0 && navigator.hardwareConcurrency < 4) {
    return false;
  }

  return true;
}

type BrowserWindowWithIdle = Window & {
  requestIdleCallback?: (
    callback: IdleRequestCallback,
    options?: IdleRequestOptions
  ) => number;
  cancelIdleCallback?: (handle: number) => void;
};

export function HeroSceneWrapper() {
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    if (!shouldLoadHeroScene()) {
      return;
    }

    const browserWindow = window as BrowserWindowWithIdle;

    if (browserWindow.requestIdleCallback) {
      const callbackId = browserWindow.requestIdleCallback(() => {
        setMounted(true);
      }, { timeout: 1200 });

      return () => {
        browserWindow.cancelIdleCallback?.(callbackId);
      };
    }

    const timeoutId = globalThis.setTimeout(() => {
      setMounted(true);
    }, 200);

    return () => {
      globalThis.clearTimeout(timeoutId);
    };
  }, []);

  if (!mounted) {
    return null;
  }

  return <HeroScene />;
}

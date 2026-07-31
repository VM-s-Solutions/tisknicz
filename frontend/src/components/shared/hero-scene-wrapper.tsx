'use client';

import dynamic from 'next/dynamic';
import { Component, useEffect, useState } from 'react';
import type { ReactNode } from 'react';

const HeroScene = dynamic(
  () => import('@/components/shared/hero-scene').then((mod) => mod.HeroScene),
  { ssr: false }
);

// A browser can refuse a WebGL context outright — hardware acceleration
// switched off, a blocklisted driver, or a sandboxed profile with the GPU
// process disabled ("GL_VENDOR = Disabled"). three.js throws from the
// WebGLRenderer constructor in that case, so probe before mounting the
// scene. Cached: live contexts are a scarce per-tab resource.
let webGlSupport: boolean | null = null;

function hasWebGlSupport(): boolean {
  if (webGlSupport !== null) {
    return webGlSupport;
  }

  try {
    const probe = document.createElement('canvas');
    const gl = probe.getContext('webgl2') ?? probe.getContext('webgl');
    // Hand the probe's context straight back instead of waiting for GC —
    // browsers cap a tab at roughly 16 of them.
    gl?.getExtension('WEBGL_lose_context')?.loseContext();
    webGlSupport = gl !== null;
  } catch {
    webGlSupport = false;
  }

  return webGlSupport;
}

function shouldLoadHeroScene(): boolean {
  if (typeof window === 'undefined') {
    return false;
  }

  if (!hasWebGlSupport()) {
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

// React still requires a class for error boundaries. Second line of defence
// behind the probe above: if context creation fails anyway (context limit
// reached, driver reset mid-session), a decorative backdrop must degrade to
// nothing rather than unmount the landing page around it.
class HeroSceneBoundary extends Component<
  { children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}

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

  return (
    <HeroSceneBoundary>
      <HeroScene />
    </HeroSceneBoundary>
  );
}

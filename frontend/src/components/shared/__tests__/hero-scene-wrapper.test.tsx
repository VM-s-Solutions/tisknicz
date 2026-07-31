import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Regression tests for the hero backdrop's WebGL gate.
 *
 * Chrome refuses a WebGL context outright when hardware acceleration is
 * off, the driver is blocklisted, or the GPU process is sandboxed away
 * ("GL_VENDOR = Disabled"). three.js throws from the WebGLRenderer
 * constructor in that case, which would tear the landing page down — so
 * the wrapper must probe for a context and skip the decorative scene
 * instead of mounting it.
 *
 * The scene module is mocked: three.js cannot run in jsdom, and what is
 * under test is the gate, not the geometry.
 */
vi.mock('@/components/shared/hero-scene', () => ({
  HeroScene: () => <div data-testid="hero-scene" />,
}));

type ProbeResult = WebGLRenderingContext | null;

function stubBrowserCapableOfTheScene(webGlContext: ProbeResult) {
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query: string) =>
      ({
        matches: query === '(min-width: 1024px)',
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }) as unknown as MediaQueryList
  );
  Object.defineProperty(navigator, 'hardwareConcurrency', {
    value: 8,
    configurable: true,
  });
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    webGlContext
  );
}

function stubWebGlContext(): WebGLRenderingContext {
  return {
    getExtension: () => ({ loseContext: vi.fn() }),
  } as unknown as WebGLRenderingContext;
}

// The probe result is cached per module instance, so each test needs a
// fresh copy of the module.
async function renderWrapper() {
  vi.resetModules();
  const { HeroSceneWrapper } = await import('../hero-scene-wrapper');
  return render(<HeroSceneWrapper />);
}

describe('HeroSceneWrapper', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('does not mount the scene when the browser refuses a WebGL context', async () => {
    stubBrowserCapableOfTheScene(null);

    const { container } = await renderWrapper();
    await act(() => vi.advanceTimersByTimeAsync(2000));

    expect(screen.queryByTestId('hero-scene')).not.toBeInTheDocument();
    expect(container).toBeEmptyDOMElement();
  });

  it('mounts the scene when a WebGL context is available', async () => {
    stubBrowserCapableOfTheScene(stubWebGlContext());

    await renderWrapper();
    await act(() => vi.advanceTimersByTimeAsync(2000));

    expect(screen.getByTestId('hero-scene')).toBeInTheDocument();
  });
});

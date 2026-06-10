'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';

/**
 * Wrapper around the official Packeta pickup-point widget v6 (T-0084a,
 * patterns.md B.2 `components/shared/ZasilkovnaWidget` slot).
 *
 * The script URL + public key + options come from the SSR-fetched
 * widget-config endpoint (T-0070) and arrive as props. The script is
 * lazily injected on first open (one `<script>` tag with load/error
 * handlers); a load failure clears the cached promise so a retry
 * re-attempts the injection (T-0084a AC-6). The widget renders its own
 * modal overlay — this wrapper only owns the trigger button.
 *
 * The script tag is the one sanctioned third-party exception: it is the
 * official widget, not an API call (the Comgate/Packeta REST APIs stay
 * backend-only per CLAUDE.md).
 */

interface PacketaPickupPoint {
  readonly id: string | number;
  readonly name: string;
}

interface PacketaGlobal {
  readonly Widget: {
    pick(
      apiKey: string,
      callback: (point: PacketaPickupPoint | null) => void,
      options?: Readonly<Record<string, string>>,
    ): void;
  };
}

declare global {
  interface Window {
    Packeta?: PacketaGlobal;
  }
}

// Module-level cache so the script is injected at most once per page
// load. Cleared on load failure so the retry affordance genuinely
// re-attempts the injection instead of replaying the rejected promise.
let packetaScriptPromise: Promise<void> | null = null;

function loadPacketaScript(scriptUrl: string): Promise<void> {
  if (typeof window !== 'undefined' && window.Packeta) {
    return Promise.resolve();
  }
  if (packetaScriptPromise) {
    return packetaScriptPromise;
  }
  packetaScriptPromise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = scriptUrl;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => {
      packetaScriptPromise = null;
      script.remove();
      reject(new Error('packeta-widget-script-load-failed'));
    };
    document.head.appendChild(script);
  });
  return packetaScriptPromise;
}

export interface ZasilkovnaWidgetProps {
  /** Widget script URL from `PickupPointWidgetConfig` (SSR prop). */
  readonly scriptUrl: string;
  /** Packeta public widget key from `PickupPointWidgetConfig`. */
  readonly publicKey: string;
  /** Widget options from widget-config (`{ country, language }`). */
  readonly options: Readonly<Record<string, string>>;
  /** Called with the chosen point; closing without a choice is a no-op. */
  readonly onPick: (point: { readonly id: string; readonly name: string }) => void;
  /** Called on script-load OR widget runtime failure. */
  readonly onError: () => void;
  /**
   * Trigger label ("Vybrat výdejní místo" / "Změnit") — owned by the
   * form because the chosen-point state lives there (T-0084a AC-5).
   */
  readonly label: string;
}

export function ZasilkovnaWidget({
  scriptUrl,
  publicKey,
  options,
  onPick,
  onError,
  label,
}: ZasilkovnaWidgetProps) {
  const [opening, setOpening] = useState(false);

  async function handleOpen() {
    if (opening) return;
    setOpening(true);
    try {
      await loadPacketaScript(scriptUrl);
    } catch {
      setOpening(false);
      onError();
      return;
    }
    const packeta = window.Packeta;
    if (!packeta) {
      setOpening(false);
      onError();
      return;
    }
    try {
      packeta.Widget.pick(
        publicKey,
        (point) => {
          setOpening(false);
          if (point) {
            onPick({ id: String(point.id), name: point.name });
          }
        },
        options,
      );
    } catch {
      setOpening(false);
      onError();
    }
  }

  return (
    <Button type="button" variant="outline" size="sm" loading={opening} onClick={handleOpen}>
      {label}
    </Button>
  );
}

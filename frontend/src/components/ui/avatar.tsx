import Image from 'next/image';
import { Icon } from '@/components/ui/icon';

type AvatarSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl';

interface AvatarProps {
  /**
   * Fully-built image URL (`buildMakerLogoUrl` / `buildAvatarUrl`), or
   * null when there is no image — the initials fallback renders instead.
   */
  readonly src?: string | null;
  /**
   * Name the initials are derived from — a company name for a logo, a
   * person's name for an avatar. Omit for an anonymous subject and the
   * tile falls back to a generic user glyph.
   */
  readonly name?: string | null;
  readonly size?: AvatarSize;
  /**
   * Accessible name for the image. Leave undefined for decorative use:
   * the tile is then `aria-hidden`, which is right whenever the adjacent
   * markup already names the same entity (a card whose heading is the
   * company name, a review byline). Passing a label opts into exposing
   * it to assistive tech instead.
   */
  readonly alt?: string;
  readonly className?: string;
}

/**
 * Identity tile: a maker's logo, a user's avatar, or — whenever there is
 * no image — the initials fallback the catalog used before uploads
 * existed. One component so an empty and a filled profile occupy exactly
 * the same box and nothing reflows when an image loads.
 *
 * Sizes are a closed set rather than a free px prop: `next/image` needs
 * intrinsic dimensions, and the box itself must be a real Tailwind class
 * (no arbitrary values, per CLAUDE.md), so the two have to be declared
 * together and stay in sync.
 */
const sizeStyles: Record<AvatarSize, { box: string; text: string; px: number; glyph: number }> = {
  xs: { box: 'h-7 w-7', text: 'text-xs', px: 28, glyph: 14 },
  sm: { box: 'h-8 w-8', text: 'text-sm', px: 32, glyph: 16 },
  md: { box: 'h-12 w-12', text: 'text-lg', px: 48, glyph: 22 },
  lg: { box: 'h-14 w-14', text: 'text-lg', px: 56, glyph: 24 },
  xl: { box: 'h-20 w-20', text: 'text-2xl', px: 80, glyph: 32 },
};

export function Avatar({ src, name, size = 'md', alt, className = '' }: AvatarProps) {
  const styles = sizeStyles[size];
  const initials = avatarInitials(name);
  const decorative = alt === undefined;

  return (
    <span
      className={`icon-tile shrink-0 overflow-hidden font-semibold ${styles.box} ${styles.text} ${className}`}
      {...(decorative ? { 'aria-hidden': true } : {})}
    >
      {src ? (
        <Image
          src={src}
          alt={decorative ? '' : alt}
          width={styles.px}
          height={styles.px}
          // The source is arbitrary user-uploaded imagery of any aspect
          // ratio; cover-crop to the square box so a panoramic logo can't
          // letterbox the tile or distort.
          className="h-full w-full object-cover"
        />
      ) : initials !== '' ? (
        initials
      ) : (
        <Icon name="user" size={styles.glyph} />
      )}
    </span>
  );
}

/**
 * Initials from the first and last word of a name — "Jan Novák" → "JN",
 * "Tiskni s.r.o." → "TS". Presentation only; returns an empty string for
 * a blank or symbol-only name so the caller falls back to the glyph.
 */
function avatarInitials(name: string | null | undefined): string {
  if (!name) return '';
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const first = words[0].charAt(0);
  const last = words.length > 1 ? words[words.length - 1].charAt(0) : '';
  return `${first}${last}`.toUpperCase();
}

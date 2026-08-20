import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeAA } from '@/lib/testing/axe';
import type { DisplaySession } from '@/lib/auth/display-session';
import { AlreadySignedIn } from '../already-signed-in';

/**
 * a11y test for the "already signed in" panel /login shows in place of
 * the form (ADR 0023 §5). Presentational Server Component — props are
 * seeded, no network. The sign-out slot is stubbed with a real
 * `<button>` so the panel is checked with its full interactive set.
 */

const session: DisplaySession = {
  userId: 'u-maker-1',
  email: 'karel.tiskar@makables.test',
  audience: 'maker',
};

describe('AlreadySignedIn a11y', () => {
  it('has no WCAG AA violations', async () => {
    const { container } = render(
      <AlreadySignedIn
        session={session}
        redirect="/objednavka?productId=p1"
        switchAccount={<button type="button">Přihlásit se jiným účtem</button>}
      />,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });
});

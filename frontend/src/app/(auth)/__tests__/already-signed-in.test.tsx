import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { DisplaySession } from '@/lib/auth/display-session';
import { AlreadySignedIn } from '../already-signed-in';

/**
 * The panel /login shows instead of a form when a session already
 * exists. Reported bug: a signed-in maker pressing "Objednat" got a
 * login screen their own account could never satisfy, and browser Back
 * kept returning them to it.
 */

const makerSession: DisplaySession = {
  userId: 'u-maker-1',
  email: 'karel.tiskar@makables.test',
  audience: 'maker',
};

const customerSession: DisplaySession = {
  userId: 'u-cust-1',
  email: 'jana.novakova@makables.test',
  audience: 'customer',
};

function continueLink(): HTMLAnchorElement {
  return screen.getByRole('link', { name: /Pokračovat/ }) as HTMLAnchorElement;
}

describe('AlreadySignedIn', () => {
  it('names the signed-in account and its role', () => {
    render(<AlreadySignedIn session={makerSession} redirect={null} switchAccount={null} />);
    expect(screen.getByText(/karel\.tiskar@makables\.test/)).toBeInTheDocument();
    expect(screen.getByText('účet makera')).toBeInTheDocument();
  });

  it('sends a maker home instead of back to the customer-only target', () => {
    render(
      <AlreadySignedIn
        session={makerSession}
        redirect="/objednavka?productId=p1"
        switchAccount={null}
      />,
    );
    expect(continueLink()).toHaveAttribute('href', '/dashboard/maker/objednavky');
  });

  it('keeps a target the session audience owns', () => {
    render(
      <AlreadySignedIn
        session={customerSession}
        redirect="/objednavka?productId=p1"
        switchAccount={null}
      />,
    );
    expect(continueLink()).toHaveAttribute('href', '/objednavka?productId=p1');
  });

  it('renders the caller-supplied sign-out slot', () => {
    render(
      <AlreadySignedIn
        session={makerSession}
        redirect={null}
        switchAccount={<button type="button">Přihlásit se jiným účtem</button>}
      />,
    );
    expect(
      screen.getByRole('button', { name: 'Přihlásit se jiným účtem' }),
    ).toBeInTheDocument();
  });
});

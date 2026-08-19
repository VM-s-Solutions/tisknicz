import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import ContactPage from '../page';

/**
 * /kontakt nese závazné identifikační údaje provozovatele, na které se
 * odkazují VOP §1 i GDPR §1. Test hlídá dvě věci: že tam jsou reálné údaje
 * ověřené proti ARES (IČO 29633443), a že se do stránky nevrátí placeholder
 * z T-0130 — ten byl na této stránce zrušen.
 */
describe('/kontakt — identifikace provozovatele', () => {
  it('renders the registered company identification', () => {
    render(
      <main>
        <ContactPage />
      </main>,
    );

    expect(screen.getByText('JVM Yore, s.r.o.')).toBeInTheDocument();
    expect(screen.getByText('29633443')).toBeInTheDocument();
    expect(screen.getByText('Neplátce DPH')).toBeInTheDocument();
    expect(screen.getByText('Příčná 1892/4, Nové Město, 110 00 Praha 1')).toBeInTheDocument();
    expect(screen.getByText('Městský soud v Praze, oddíl C, vložka 449138')).toBeInTheDocument();
  });

  it('exposes the operator e-mail as a mailto link', () => {
    render(
      <main>
        <ContactPage />
      </main>,
    );

    expect(screen.getByRole('link', { name: 'makables@jvm-yore.com' })).toHaveAttribute(
      'href',
      'mailto:makables@jvm-yore.com',
    );
  });

  it('no longer carries the T-0130 placeholder copy', () => {
    const { container } = render(
      <main>
        <ContactPage />
      </main>,
    );

    expect(container.textContent).not.toMatch(/PLACEHOLDER|doplní se před spuštěním/);
  });
});

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { axeAA } from '@/lib/testing/axe';
import { Dialog } from '../dialog';

/**
 * T-0176 (audit ADM-M10): three admin modal shells carried a comment
 * claiming a "focus-trap pattern" while implementing none — focus stayed
 * on the trigger behind the backdrop and Tab walked into the obscured
 * page. This is the shared replacement, so the trap is pinned here once.
 */
function renderDialog(props: Partial<Parameters<typeof Dialog>[0]> = {}) {
  const onClose = vi.fn();
  render(
    <>
      <button type="button">outside</button>
      <Dialog titleId="dlg" title="Refundovat objednávku" onClose={onClose} {...props}>
        <button type="button">first</button>
        <button type="button">last</button>
      </Dialog>
    </>,
  );
  return { onClose };
}

describe('Dialog', () => {
  it('moves focus inside on mount', () => {
    renderDialog();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
  });

  it('wraps Tab from the last element back to the first', () => {
    renderDialog();
    const first = screen.getByRole('button', { name: 'first' });
    const last = screen.getByRole('button', { name: 'last' });

    last.focus();
    fireEvent.keyDown(window, { key: 'Tab' });

    expect(document.activeElement).toBe(first);
  });

  it('wraps Shift+Tab from the first element to the last', () => {
    renderDialog();
    const last = screen.getByRole('button', { name: 'last' });

    screen.getByRole('button', { name: 'first' }).focus();
    fireEvent.keyDown(window, { key: 'Tab', shiftKey: true });

    expect(document.activeElement).toBe(last);
  });

  it('pulls focus back in when it escaped to the page behind', () => {
    renderDialog();
    screen.getByRole('button', { name: 'outside' }).focus();

    fireEvent.keyDown(window, { key: 'Tab' });

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
  });

  it('closes on Escape, but not while a request is in flight', () => {
    const { onClose } = renderDialog({ closeDisabled: true });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });

  it('closes on Escape when idle', () => {
    const { onClose } = renderDialog();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('has no axe violations', async () => {
    const { container } = render(
      <Dialog titleId="dlg" title="Refundovat objednávku" onClose={() => {}}>
        <button type="button">confirm</button>
      </Dialog>,
    );
    expect(await axeAA(container)).toHaveNoViolations();
  });
});
